using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Contracts;

namespace Forum.Modules.Social.Application;

/// <summary>
/// Implements <see cref="ISocialVisibility"/> for the realtime hub: one active-participant check answers every
/// social scope (a group chat's conversation id IS the group id, and membership writes through to the seats), so
/// a kick/leave gates the very next push.
/// </summary>
internal sealed class SocialVisibilityReader : ISocialVisibility
{
    private readonly IConversationRepository _conversations;

    public SocialVisibilityReader(IConversationRepository conversations) => _conversations = conversations;

    public async Task<bool> IsConversationParticipantAsync(
        Ulid conversationId, Ulid userId, CancellationToken cancellationToken) =>
        await _conversations.GetParticipantAsync(conversationId, userId, cancellationToken) is { IsActive: true };
}
