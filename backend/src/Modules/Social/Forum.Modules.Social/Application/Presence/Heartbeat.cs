using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Presence;

/// <summary>
/// Upserts the caller's heartbeat. The SPA calls this on a ~30 s timer while the tab is active — a REST beat was
/// chosen over deriving presence from the WebSocket lifecycle because it needs zero Bootstrap coupling, survives
/// socket flaps, and sits exactly on the seam the scoped Redis session will swap (see IPresenceStore).
/// </summary>
internal sealed record HeartbeatCommand : ICommand;

internal sealed class HeartbeatCommandHandler : ICommandHandler<HeartbeatCommand>
{
    private readonly IPresenceStore _presence;
    private readonly ICurrentUser _currentUser;

    public HeartbeatCommandHandler(IPresenceStore presence, ICurrentUser currentUser)
    {
        _presence = presence;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(HeartbeatCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure(SocialErrors.AuthenticationRequired);
        }

        await _presence.HeartbeatAsync(userId, cancellationToken);
        return Result.Success();
    }
}
