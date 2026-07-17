using Forum.Common.Messaging;

namespace Forum.Modules.Social.Contracts.IntegrationEvents;

/// <summary>
/// Published when a message is sent. Routing facts for the realtime hub: <paramref name="ConversationType"/> is
/// the wire string ("direct"/"group"); for direct conversations <paramref name="DirectParticipantIds"/> carries
/// both participants (fixed at creation) so the hub can hit their user views for the unread badge — group chats
/// route via the group view instead (no per-member fan-out). Payload content NEVER rides the socket (ADR 0010).
/// </summary>
public sealed record MessageSentIntegrationEvent(
    Ulid EventId, Ulid MessageId, Ulid ConversationId, string ConversationType, Ulid SenderId,
    IReadOnlyList<Ulid> DirectParticipantIds, DateTimeOffset OccurredOnUtc) : IIntegrationEvent;

/// <summary>Published when a message body is edited.</summary>
public sealed record MessageEditedIntegrationEvent(
    Ulid EventId, Ulid MessageId, Ulid ConversationId, string ConversationType, Ulid SenderId,
    IReadOnlyList<Ulid> DirectParticipantIds, DateTimeOffset OccurredOnUtc) : IIntegrationEvent;

/// <summary>Published when a message is tombstoned. Files detaches the message's images on this.</summary>
public sealed record MessageDeletedIntegrationEvent(
    Ulid EventId, Ulid MessageId, Ulid ConversationId, string ConversationType, Ulid SenderId,
    IReadOnlyList<Ulid> DirectParticipantIds, DateTimeOffset OccurredOnUtc) : IIntegrationEvent;
