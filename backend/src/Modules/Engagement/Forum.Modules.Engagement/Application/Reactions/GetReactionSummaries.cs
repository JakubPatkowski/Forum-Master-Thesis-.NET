using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Engagement.Application.Abstractions;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Engagement.Application.Reactions;

/// <summary>
/// Batch reaction summaries for a feed page (no N+1: the SPA fetches the feed from Content, then patches counts
/// in from here with one call). Raw inputs are validated here: unknown target type, empty/oversized id lists and
/// malformed ULIDs are 422s. Duplicate ids collapse; the response carries one row per distinct requested id.
/// </summary>
internal sealed record GetReactionSummariesQuery(string? TargetType, IReadOnlyList<string> TargetIds)
    : IQuery<IReadOnlyList<ReactionSummaryResponse>>;

internal sealed class GetReactionSummariesQueryHandler
    : IQueryHandler<GetReactionSummariesQuery, IReadOnlyList<ReactionSummaryResponse>>
{
    private readonly IEngagementQueries _queries;
    private readonly ICurrentUser _currentUser;

    public GetReactionSummariesQueryHandler(IEngagementQueries queries, ICurrentUser currentUser)
    {
        _queries = queries;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<ReactionSummaryResponse>>> Handle(
        GetReactionSummariesQuery query, CancellationToken cancellationToken)
    {
        if (!ReactionTargets.TryParse(query.TargetType, out var targetType))
        {
            return Result.Failure<IReadOnlyList<ReactionSummaryResponse>>(EngagementErrors.InvalidTargetType);
        }

        if (query.TargetIds.Count == 0)
        {
            return Result.Failure<IReadOnlyList<ReactionSummaryResponse>>(EngagementErrors.NoTargets);
        }

        if (query.TargetIds.Count > EngagementErrors.MaxBatchTargets)
        {
            return Result.Failure<IReadOnlyList<ReactionSummaryResponse>>(EngagementErrors.TooManyTargets);
        }

        var targetIds = new List<Ulid>(query.TargetIds.Count);
        foreach (var raw in query.TargetIds)
        {
            if (!Ulid.TryParse(raw, CultureInfo.InvariantCulture, out var targetId))
            {
                return Result.Failure<IReadOnlyList<ReactionSummaryResponse>>(EngagementErrors.InvalidTargetId);
            }

            if (!targetIds.Contains(targetId))
            {
                targetIds.Add(targetId);
            }
        }

        return Result.Success(await _queries.GetSummariesAsync(
            targetType, targetIds, _currentUser.Id, cancellationToken));
    }
}
