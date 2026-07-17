namespace Forum.Modules.Social.Domain.Friendships;

/// <summary>
/// A peer-to-peer, unilateral block (composite key, no surrogate id — Reaction precedent). Deliberately NOT named
/// "UserBlock": Identity's <c>UserStatus.Blocked</c>/<c>UserBlockedIntegrationEvent</c> is an ADMIN moderation ban,
/// a completely different concept. A block suppresses friend requests, DMs and group invites between the pair in
/// BOTH directions (a blocker messaging their own blocked party would be nonsense UX), and creating one deletes
/// any existing friendship and pending requests/invites between the two.
/// </summary>
internal sealed class SocialBlock
{
    // EF materialization.
    private SocialBlock()
    {
    }

    public SocialBlock(Ulid blockerId, Ulid blockedId, DateTimeOffset createdOnUtc)
    {
        BlockerId = blockerId;
        BlockedId = blockedId;
        CreatedOnUtc = createdOnUtc;
    }

    public Ulid BlockerId { get; private set; }

    public Ulid BlockedId { get; private set; }

    public DateTimeOffset CreatedOnUtc { get; private set; }
}
