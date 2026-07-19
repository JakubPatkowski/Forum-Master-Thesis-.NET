namespace Forum.Modules.Social.Domain.Conversations;

using Forum.SharedKernel.Domain;

/// <summary>
/// The single messaging pipeline for both DMs and group chat. A group's conversation reuses the Group's own ULID
/// as its id (Type = Group, created in the same transaction as the group) — no nullable-FK indirection. Direct
/// conversations are get-or-created lazily when a chat is opened; <see cref="DirectKey"/>
/// (<c>"{loUlid}:{hiUlid}"</c>, null for group chats) plus a partial unique index makes that race-safe and
/// guarantees one conversation per pair.
/// </summary>
internal sealed class Conversation : AggregateRoot<Ulid>
{
    // EF materialization.
    private Conversation()
    {
    }

    private Conversation(Ulid id, ConversationType type, string? directKey) : base(id)
    {
        Type = type;
        DirectKey = directKey;
    }

    public ConversationType Type { get; private set; }

    /// <summary>Canonical unordered pair key for Direct conversations; null for group chats.</summary>
    public string? DirectKey { get; private set; }

    public static Conversation CreateDirect(Ulid userA, Ulid userB) =>
        new(Ulid.NewUlid(), ConversationType.Direct, BuildDirectKey(userA, userB));

    /// <summary>The group chat: same ULID as the group itself.</summary>
    public static Conversation CreateForGroup(Ulid groupId) =>
        new(groupId, ConversationType.Group, directKey: null);

    public static string BuildDirectKey(Ulid userA, Ulid userB)
    {
        var (lo, hi) = string.CompareOrdinal(userA.ToString(), userB.ToString()) < 0 ? (userA, userB) : (userB, userA);
        return $"{lo}:{hi}";
    }

    /// <summary>Offline-seeder constructor: deterministic/explicit id + audit, no events.</summary>
    internal static Conversation Seed(Ulid id, ConversationType type, string? directKey, DateTimeOffset createdOnUtc)
    {
        var conversation = new Conversation(id, type, directKey);
        conversation.SetCreated(createdOnUtc, null);
        return conversation;
    }
}

/// <summary>Stored as text (Content's Visibility precedent), never as a PG enum.</summary>
internal enum ConversationType
{
    Direct,
    Group,
}
