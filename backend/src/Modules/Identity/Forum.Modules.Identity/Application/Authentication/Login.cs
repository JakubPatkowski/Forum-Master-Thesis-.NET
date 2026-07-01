using FluentValidation;

using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Identity.Application.Abstractions;
using Forum.Modules.Identity.Application.Validation;
using Forum.Modules.Identity.Domain.Tokens;
using Forum.SharedKernel.Results;

using Microsoft.Extensions.Options;

namespace Forum.Modules.Identity.Application.Authentication;

/// <summary>Authenticates by email + password and issues an access JWT plus a rotating refresh token.</summary>
internal sealed record LoginCommand(string Email, string Password, string? Ip, string? UserAgent)
    : ICommand<AuthTokensResponse>;

internal sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(static command => command.Email).NotEmpty();
        RuleFor(static command => command.Password).NotEmpty();
    }
}

internal sealed class LoginCommandHandler : ICommandHandler<LoginCommand, AuthTokensResponse>
{
    private readonly IValidator<LoginCommand> _validator;
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IAuthorizationStore _authorization;
    private readonly IPasswordVerifier _passwordVerifier;
    private readonly IJwtTokenService _jwt;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;
    private readonly JwtOptions _jwtOptions;

    public LoginCommandHandler(
        IValidator<LoginCommand> validator,
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IAuthorizationStore authorization,
        IPasswordVerifier passwordVerifier,
        IJwtTokenService jwt,
        IRefreshTokenService refreshTokenService,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        IOptions<JwtOptions> jwtOptions)
    {
        _validator = validator;
        _users = users;
        _refreshTokens = refreshTokens;
        _authorization = authorization;
        _passwordVerifier = passwordVerifier;
        _jwt = jwt;
        _refreshTokenService = refreshTokenService;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _jwtOptions = jwtOptions.Value;
    }

    public async Task<Result<AuthTokensResponse>> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        if (await _validator.ValidateToErrorAsync(command, cancellationToken) is { } validationError)
        {
            return Result.Failure<AuthTokensResponse>(validationError);
        }

        var user = await _users.GetByEmailAsync(command.Email.Trim(), cancellationToken);

        // Constant-time, non-revealing: a missing user still spends a verify, and every failure returns one error.
        if (user is null)
        {
            _passwordVerifier.VerifyDummy(command.Password);
            return Result.Failure<AuthTokensResponse>(AuthErrors.InvalidCredentials);
        }

        if (!_passwordVerifier.Verify(user.PasswordHash, command.Password) || !user.IsActive)
        {
            return Result.Failure<AuthTokensResponse>(AuthErrors.InvalidCredentials);
        }

        var roles = await _authorization.GetRoleNamesForUserAsync(user.Id, cancellationToken);
        var access = _jwt.Issue(user, roles);

        var now = _clock.GetUtcNow();
        var plainRefresh = _refreshTokenService.Generate();
        var refreshExpires = now.AddDays(_jwtOptions.RefreshTokenDays);
        var token = RefreshToken.IssueNew(
            user.Id, _refreshTokenService.Hash(plainRefresh), refreshExpires, now, command.Ip, command.UserAgent);

        _refreshTokens.Add(token);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new AuthTokensResponse(
            access.Value, access.ExpiresOnUtc, plainRefresh, refreshExpires));
    }
}
