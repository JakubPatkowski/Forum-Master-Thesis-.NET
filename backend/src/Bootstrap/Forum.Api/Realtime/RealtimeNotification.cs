namespace Forum.Api.Realtime;

/// <summary>
/// A mapped integration event on its way to sockets: the wire payload plus the routing facts the hub matches
/// subscriptions and re-checks visibility against. Every relayed event resolves to a category — the visibility
/// gate is always evaluated at category scope, exactly like Content's and Engagement's own write gates.
/// </summary>
/// <param name="Payload">The compact envelope actually sent to sockets.</param>
/// <param name="CategoryId">The owning category — the scope of the per-push visibility re-check.</param>
/// <param name="ThreadId">The thread whose view cares about this change (the thread itself for thread events).</param>
/// <param name="ActorUserId">The acting user for reaction events — lets that user's other devices sync toggle state.</param>
internal sealed record RealtimeNotification(
    ChangeNotification Payload, Ulid CategoryId, Ulid? ThreadId, Ulid? ActorUserId);
