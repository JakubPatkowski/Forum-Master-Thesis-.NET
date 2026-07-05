using Forum.Modules.Engagement.Domain.Reactions;

namespace Forum.Modules.Engagement.Application.Abstractions;

/// <summary>
/// Resolves a reaction target to the category context the like gate is authorized against. Implemented as a
/// read-only cross-schema query into <c>forum_content</c> — the same "a later module may read-join an earlier
/// module's tables" precedent Content's views use over <c>forum_identity.users</c> (Engagement migrates last).
/// Soft-deleted targets (or targets under a deleted thread/category) resolve to null.
/// </summary>
internal interface IReactionTargetReader
{
    Task<ReactionTarget?> GetAsync(ReactionTargetType targetType, Ulid targetId, CancellationToken cancellationToken);
}

/// <summary>
/// The owning category of a reaction target (scope for the permission check plus the visibility gate) and its
/// containing thread — the thread itself for thread targets — so reaction events can be routed to thread views.
/// </summary>
internal sealed record ReactionTarget(Ulid CategoryId, Ulid CategoryOwnerId, bool CategoryIsPrivate, Ulid ThreadId);
