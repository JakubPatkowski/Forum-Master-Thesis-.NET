using Forum.Modules.Engagement.Application.Reactions;
using Forum.Modules.Engagement.Application.Stats;
using Forum.Modules.Engagement.Domain.Reactions;

namespace Forum.Modules.Engagement.Application.Abstractions;

/// <summary>
/// The Engagement read side: O(1) counter lookups from <c>reaction_counts</c> (never an aggregate scan of
/// <c>reactions</c>) plus the <c>user_stats_v</c> view. A null viewer reads as "not reacted".
/// </summary>
internal interface IEngagementQueries
{
    Task<ReactionSummaryResponse> GetSummaryAsync(
        ReactionTargetType targetType, Ulid targetId, Ulid? viewerId, CancellationToken cancellationToken);

    /// <summary>Batch variant for feeds (no N+1): one row per requested id, zero-count when nobody reacted.</summary>
    Task<IReadOnlyList<ReactionSummaryResponse>> GetSummariesAsync(
        ReactionTargetType targetType, IReadOnlyList<Ulid> targetIds, Ulid? viewerId, CancellationToken cancellationToken);

    Task<UserStatsResponse?> GetUserStatsAsync(Ulid userId, CancellationToken cancellationToken);
}
