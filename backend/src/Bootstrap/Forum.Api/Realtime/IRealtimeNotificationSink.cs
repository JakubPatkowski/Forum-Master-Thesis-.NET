namespace Forum.Api.Realtime;

/// <summary>
/// Where the change-feed consumer hands a mapped notification. In production this is the dispatcher pushing to
/// this replica's sockets; tests plug a recorder to prove every replica's feed receives every event.
/// </summary>
internal interface IRealtimeNotificationSink
{
    Task PublishAsync(RealtimeNotification notification, CancellationToken cancellationToken);
}
