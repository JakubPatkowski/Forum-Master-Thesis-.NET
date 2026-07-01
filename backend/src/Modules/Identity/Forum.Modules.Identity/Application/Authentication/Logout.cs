using Forum.Common.Cqrs;
using Forum.Modules.Identity.Application.Abstractions;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Identity.Application.Authentication;

/// <summary>Revokes a single refresh token (this session). Idempotent — an unknown token still succeeds.</summary>
internal sealed record LogoutCommand(string? RefreshToken) : ICommand;

internal sealed class LogoutCommandHandler : ICommandHandler<LogoutCommand>
{
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly IUnitOfWork _unitOfWork;

    public LogoutCommandHandler(
        IRefreshTokenRepository refreshTokens, IRefreshTokenService refreshTokenService, IUnitOfWork unitOfWork)
    {
        _refreshTokens = refreshTokens;
        _refreshTokenService = refreshTokenService;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(LogoutCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            return Result.Success();
        }

        var token = await _refreshTokens.GetByHashAsync(_refreshTokenService.Hash(command.RefreshToken), cancellationToken);
        if (token is not null)
        {
            token.Revoke();
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}

/// <summary>Revokes every active refresh token for a user (sign out everywhere).</summary>
internal sealed record LogoutAllCommand(Ulid UserId) : ICommand;

internal sealed class LogoutAllCommandHandler : ICommandHandler<LogoutAllCommand>
{
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;

    public LogoutAllCommandHandler(IRefreshTokenRepository refreshTokens, IUnitOfWork unitOfWork)
    {
        _refreshTokens = refreshTokens;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(LogoutAllCommand command, CancellationToken cancellationToken)
    {
        await _refreshTokens.RevokeAllForUserAsync(command.UserId, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
