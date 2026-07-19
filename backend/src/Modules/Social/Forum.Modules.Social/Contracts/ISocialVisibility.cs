namespace Forum.Modules.Social.Contracts;

/// <summary>
/// Social's visibility surface for the WebSocket hub (the <c>IContentVisibility</c> mirror). One method covers
/// every social scope: a group chat's conversation id IS the group id and membership changes write through to
/// <c>conversation_participants</c> in the same transaction, so "is this user an active participant of this
/// conversation" answers both "may they see this group's events" and "may they see this DM" — one authorization
/// code path, re-checked on every push so a leave/kick gates the very next event.
/// </summary>
public interface ISocialVisibility
{
    /// <summary>True when the user currently holds an active (non-left) seat in the conversation.</summary>
    Task<bool> IsConversationParticipantAsync(Ulid conversationId, Ulid userId, CancellationToken cancellationToken);
}
