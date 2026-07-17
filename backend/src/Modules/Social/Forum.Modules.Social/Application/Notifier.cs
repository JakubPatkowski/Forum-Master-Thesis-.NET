using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Contracts.IntegrationEvents;
using Forum.Modules.Social.Domain.Notifications;

namespace Forum.Modules.Social.Application;

/// <summary>
/// Creates the durable notification row AND queues its <see cref="NotificationCreatedIntegrationEvent"/> in one
/// step, so the bell's database truth and its realtime ping can never diverge (both commit with the handler's
/// transaction; the push itself carries identity + routing only, per ADR 0010).
/// </summary>
internal sealed class Notifier
{
    private readonly INotificationRepository _notifications;
    private readonly IOutboxWriter _outbox;

    public Notifier(INotificationRepository notifications, IOutboxWriter outbox)
    {
        _notifications = notifications;
        _outbox = outbox;
    }

    public void Notify(Ulid userId, string kind, Ulid? actorId, Ulid? targetId, DateTimeOffset onUtc)
    {
        var notification = new Notification(Ulid.NewUlid(), userId, kind, actorId, targetId, onUtc);
        _notifications.Add(notification);
        _outbox.Enqueue(new NotificationCreatedIntegrationEvent(Ulid.NewUlid(), notification.Id, userId, onUtc));
    }
}
