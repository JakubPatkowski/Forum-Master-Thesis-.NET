using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Identity.Application.Abstractions;
using Forum.Modules.Identity.Domain.Tokens;
using Forum.SharedKernel.Results;

using Microsoft.Extensions.Options;

namespace Forum.Modules.Identity.Application.Authentication;

/// <summary>Rotates a refresh token: validate → rotate → issue a new pair. Reuse of a spent token revokes the whole family.</summary>
internal sealed record RefreshTokenCommand(string RefreshToken, string? Ip, string? UserAgent)
    : ICommand<AuthTokensResponse>;

internal sealed class RefreshTokenCommandHandler : ICommandHandler<RefreshTokenCommand, AuthTokensResponse>
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IAuthorizationStore _authorization;
    private readonly IJwtTokenService _jwt;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;
    private readonly JwtOptions _jwtOptions;

    public RefreshTokenCommandHandler(
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IAuthorizationStore authorization,
        IJwtTokenService jwt,
        IRefreshTokenService refreshTokenService,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        IOptions<JwtOptions> jwtOptions)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _authorization = authorization;
        _jwt = jwt;
        _refreshTokenService = refreshTokenService;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _jwtOptions = jwtOptions.Value;
    }

    public async Task<Result<AuthTokensResponse>> Handle(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return Result.Failure<AuthTokensResponse>(AuthErrors.InvalidRefreshToken);
        }

        var hash = _refreshTokenService.Hash(command.RefreshToken);
        var token = await _refreshTokens.GetByHashAsync(hash, cancellationToken);
        if (token is null)
        {
            return Result.Failure<AuthTokensResponse>(AuthErrors.InvalidRefreshToken);
        }

        var now = _clock.GetUtcNow();

        // Reuse/theft detection: a presented token that was already rotated or revoked means the family is compromised.
        if (token.Status != RefreshTokenStatus.Active)
        {
            await _refreshTokens.RevokeFamilyAsync(token.FamilyId, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure<AuthTokensResponse>(AuthErrors.InvalidRefreshToken);
        }

        if (token.ExpiresOnUtc <= now)
        {
            return Result.Failure<AuthTokensResponse>(AuthErrors.InvalidRefreshToken);
        }

        var user = await _users.GetByIdAsync(token.UserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            await _refreshTokens.RevokeFamilyAsync(token.FamilyId, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Result.Failure<AuthTokensResponse>(AuthErrors.InvalidRefreshToken);
        }

        var plainRefresh = _refreshTokenService.Generate();
        var refreshExpires = now.AddDays(_jwtOptions.RefreshTokenDays);
        var rotated = token.IssueNextInFamily(
            _refreshTokenService.Hash(plainRefresh), refreshExpires, now, command.Ip, command.UserAgent);

        token.Rotate(rotated.Id);
        _refreshTokens.Add(rotated);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var roles = await _authorization.GetRoleNamesForUserAsync(user.Id, cancellationToken);
        var access = _jwt.Issue(user, roles);

        return Result.Success(new AuthTokensResponse(
            access.Value, access.ExpiresOnUtc, plainRefresh, refreshExpires));
    }
}
