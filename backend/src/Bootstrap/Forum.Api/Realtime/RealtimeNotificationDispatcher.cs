using System.Text.Json;

using Forum.Common.Security;
using Forum.Common.Telemetry;
using Forum.Modules.Content.Contracts;

namespace Forum.Api.Realtime;

/// <summary>
/// Pushes a notification to this replica's matching sockets, re-checking visibility on EVERY push (ADR 0010):
/// a private category's changes go only to subscribers who are its owner or hold <c>moderate</c> at that
/// category's scope — the same gate Content and Engagement enforce on writes, resolved fresh against the SQL
/// ACL so a permission revoked mid-connection takes effect on the very next event.
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

        // One category lookup per notification; a vanished (deleted) category means nothing is pushed at all.
        var access = await scope.ServiceProvider.GetRequiredService<IContentVisibility>()
            .GetCategoryAccessAsync(notification.CategoryId, cancellationToken);
        if (access is null)
        {
            return;
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(notification.Payload, RealtimeJson.Options);
        var permissions = access.IsPrivate
            ? scope.ServiceProvider.GetRequiredService<IPermissionService>()
            : null;

        foreach (var connection in subscribers)
        {
            if (permissions is not null && !await MaySeeAsync(permissions, connection.UserId, access, cancellationToken))
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

    private static Task<bool> MaySeeAsync(
        IPermissionService permissions, Ulid userId, CategoryAccess access, CancellationToken cancellationToken) =>
        userId == access.OwnerId
            ? Task.FromResult(true)
            : permissions.HasPermissionAsync(
                userId, Permissions.Moderate, PermissionScopes.Category, access.CategoryId, cancellationToken);
}
