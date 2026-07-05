using System.Collections.Concurrent;

namespace Forum.Api.Realtime;

/// <summary>
/// The sockets connected to THIS replica. Each replica pushes only to its own registry — cross-replica
/// coverage comes from the bus fanning every event out to every replica's queue (ADR 0010), not from shared state.
/// </summary>
internal sealed class RealtimeConnectionRegistry
{
    private readonly ConcurrentDictionary<Guid, RealtimeConnection> _connections = new();

    public void Add(RealtimeConnection connection) => _connections[connection.Id] = connection;

    public void Remove(RealtimeConnection connection) => _connections.TryRemove(connection.Id, out _);

    /// <summary>A snapshot of the connections subscribed to something the notification touches.</summary>
    public IReadOnlyList<RealtimeConnection> Match(RealtimeNotification notification)
    {
        List<RealtimeConnection>? matched = null;
        foreach (var connection in _connections.Values)
        {
            if (connection.Subscriptions.Matches(notification))
            {
                (matched ??= []).Add(connection);
            }
        }

        return matched ?? (IReadOnlyList<RealtimeConnection>)[];
    }
}
