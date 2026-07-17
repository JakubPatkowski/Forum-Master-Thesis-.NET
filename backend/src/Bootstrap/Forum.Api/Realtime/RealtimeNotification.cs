namespace Forum.Api.Realtime;

/// <summary>How the dispatcher authorizes a notification before pushing (re-checked on EVERY push, ADR 0010).</summary>
internal enum RealtimeVisibilityKind
{
    /// <summary>Content's category rule: public passes, private needs owner-or-moderate at the category scope.</summary>
    Category,

    /// <summary>Social's participant rule: every subscriber must hold an active seat in the conversation
    /// (a group chat's conversation id IS the group id, so this covers group-scoped events too).</summary>
    Conversation,

    /// <summary>No per-push check: the routes are user views only, and those are subscribe-time self-gated.</summary>
    TargetUsers,
}

/// <summary>The visibility scope instance a notification is checked against.</summary>
internal readonly record struct RealtimeVisibility(RealtimeVisibilityKind Kind, Ulid Id)
{
    public static RealtimeVisibility Category(Ulid categoryId) => new(RealtimeVisibilityKind.Category, categoryId);

    public static RealtimeVisibility Conversation(Ulid conversationId) =>
        new(RealtimeVisibilityKind.Conversation, conversationId);

    public static RealtimeVisibility TargetUsers { get; } = new(RealtimeVisibilityKind.TargetUsers, default);
}

/// <summary>
/// A mapped integration event on its way to sockets: the wire payload, the visibility scope the dispatcher
/// re-checks per push, and the subscription views it routes to (matching is pure set intersection). The original
/// Phase 7 shape was Content-specific (CategoryId/ThreadId/ActorUserId); with a second module feeding the hub the
/// routing facts became data (<see cref="Routes"/>) built by <see cref="RealtimeEventMap"/> — SubscriptionSet no
/// longer knows what a "category" or "conversation" is (ADR 0011).
/// </summary>
internal sealed record RealtimeNotification(
    ChangeNotification Payload,
    RealtimeVisibility Visibility,
    IReadOnlyList<SubscriptionView> Routes);
