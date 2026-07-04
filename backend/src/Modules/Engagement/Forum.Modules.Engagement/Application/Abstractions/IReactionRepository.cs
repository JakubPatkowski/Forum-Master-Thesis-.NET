using Forum.Modules.Engagement.Domain.Reactions;

namespace Forum.Modules.Engagement.Application.Abstractions;

/// <summary>Write-side access to the reactions table. Counts are maintained by the DB trigger, never here.</summary>
internal interface IReactionRepository
{
    Task<Reaction?> GetAsync(
        Ulid userId, ReactionTargetType targetType, Ulid targetId, string reactionType, CancellationToken cancellationToken);

    void Add(Reaction reaction);

    void Remove(Reaction reaction);

    /// <summary>
    /// Bulk-removes every reaction on a target (deletion cascade). Executes immediately as a set-based
    /// DELETE; the row trigger keeps <c>reaction_counts</c> in step. Returns the number of rows removed.
    /// </summary>
    Task<int> DeleteAllForTargetAsync(ReactionTargetType targetType, Ulid targetId, CancellationToken cancellationToken);
}
