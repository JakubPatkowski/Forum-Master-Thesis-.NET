using Forum.Modules.Social.Domain.Conversations;

namespace Forum.Modules.Social.Application.Messaging;

/// <summary>Wire facts the message integration events carry for realtime routing.</summary>
internal static class ConversationWire
{
    public const string Direct = "direct";
    public const string Group = "group";

    public static string TypeOf(Conversation conversation) =>
        conversation.Type == ConversationType.Direct ? Direct : Group;

    /// <summary>Both seats for a DM (fixed at creation — the hub routes to their user views); empty for group
    /// chats, which route via the group view instead of per-member fan-out.</summary>
    public static IReadOnlyList<Ulid> DirectParticipants(
        Conversation conversation, IReadOnlyList<ConversationParticipant> activeParticipants) =>
        conversation.Type == ConversationType.Direct
            ? [.. activeParticipants.Select(static participant => participant.UserId)]
            : [];
}
