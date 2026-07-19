using System.Text.Json;

using Forum.Common.Security;
using Forum.Common.Telemetry;
using Forum.Modules.Content.Contracts;
using Forum.Modules.Social.Contracts;

namespace Forum.Api.Realtime;

/// <summary>
/// Pushes a notification to this replica's matching sockets, re-checking visibility on EVERY push (ADR 0010) by
/// the notification's visibility kind (ADR 0011): Category → Content's private-category rule (owner or
/// <c>moderate</c> at that scope, same gate as the write side); Conversation → Social's active-participant rule
/// (one check covers DMs AND groups — a kick/leave gates the very next event); TargetUsers → nothing, because
/// those routes are user views and the socket protocol only lets a user subscribe to their OWN user view.
/// </summary>
internal sealed class RealtimeNotificationDispatcher : IRealtimeNotificationSink
{
    private readonly RealtimeConnectionRegistry _registry;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ForumMetrics _metrics;
    private readonly ILogger<RealtimeNotificationDispatcher> _logger;

    public RealtimeNotificationDispatcher(
        RealtimeConnectionRegistry registry,
        IServiceScopeFactory scopeFactory,
        ForumMetrics metrics,
        ILogger<RealtimeNotificationDispatcher> logger)
    {
        _registry = registry;
        _scopeFactory = scopeFactory;
        _metrics = metrics;
        _logger = logger;
    }

    public async Task PublishAsync(RealtimeNotification notification, CancellationToken cancellationToken)
    {
        var subscribers = _registry.Match(notification);
        if (subscribers.Count == 0)
        {
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();

        // Resolve the per-subscriber gate once per notification: null = push to every matched socket,
        // NobodySees = drop the notification entirely (the scope's aggregate vanished).
        var maySee = await ResolveGateAsync(scope.ServiceProvider, notification.Visibility, cancellationToken);
        if (ReferenceEquals(maySee, NobodySees))
        {
            return;
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(notification.Payload, RealtimeJson.Options);
        foreach (var connection in subscribers)
        {
            if (maySee is not null && !await maySee(connection.UserId))
            {
                continue;
            }

            if (await connection.TrySendAsync(payload, cancellationToken))
            {
                _metrics.WsPushSent();
            }
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Dispatched {Entity}/{Type} for {Id} to up to {Count} subscriber(s).",
                notification.Payload.Entity, notification.Payload.Type, notification.Payload.Id, subscribers.Count);
        }
    }

    /// <summary>Sentinel gate meaning "the scope no longer exists — push to nobody".</summary>
    private static readonly Func<Ulid, Task<bool>> NobodySees = static _ => Task.FromResult(false);

    private static async Task<Func<Ulid, Task<bool>>?> ResolveGateAsync(
        IServiceProvider services, RealtimeVisibility visibility, CancellationToken cancellationToken)
    {
        switch (visibility.Kind)
        {
            case RealtimeVisibilityKind.Category:
                {
                    // One category lookup per notification; a vanished (deleted) category means nothing is pushed.
                    var access = await services.GetRequiredService<IContentVisibility>()
                        .GetCategoryAccessAsync(visibility.Id, cancellationToken);
                    if (access is null)
                    {
                        return NobodySees;
                    }

                    if (!access.IsPrivate)
                    {
                        return null;
                    }

                    var permissions = services.GetRequiredService<IPermissionService>();
                    return userId => userId == access.OwnerId
                        ? Task.FromResult(true)
                        : permissions.HasPermissionAsync(
                            userId, Permissions.Moderate, PermissionScopes.Category, access.CategoryId, cancellationToken);
                }

            case RealtimeVisibilityKind.Conversation:
                {
                    // Membership/participation is the whole rule — no public case exists for chats or group events.
                    var social = services.GetRequiredService<ISocialVisibility>();
                    return userId => social.IsConversationParticipantAsync(visibility.Id, userId, cancellationToken);
                }

            default:
                return null; // TargetUsers: user views are subscribe-time self-gated.
        }
    }
}
