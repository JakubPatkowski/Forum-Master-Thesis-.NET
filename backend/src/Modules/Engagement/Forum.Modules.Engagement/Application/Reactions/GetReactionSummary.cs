using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Engagement.Application.Abstractions;
using Forum.Modules.Engagement.Domain.Reactions;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Engagement.Application.Reactions;

/// <summary>
/// Reads one target's reaction summary (anonymous allowed; the viewer state is then false). Deliberately no
/// existence check against Content: a target nobody reacted to and a nonexistent target both read as zero,
/// which keeps the read a pure primary-key lookup.
/// </summary>
internal sealed record GetReactionSummaryQuery(ReactionTargetType TargetType, Ulid TargetId)
    : IQuery<ReactionSummaryResponse>;

internal sealed class GetReactionSummaryQueryHandler
    : IQueryHandler<GetReactionSummaryQuery, ReactionSummaryResponse>
{
    private readonly IEngagementQueries _queries;
    private readonly ICurrentUser _currentUser;

    public GetReactionSummaryQueryHandler(IEngagementQueries queries, ICurrentUser currentUser)
    {
        _queries = queries;
        _currentUser = currentUser;
    }

    public async Task<Result<ReactionSummaryResponse>> Handle(
        GetReactionSummaryQuery query, CancellationToken cancellationToken) =>
        Result.Success(await _queries.GetSummaryAsync(
            query.TargetType, query.TargetId, _currentUser.Id, cancellationToken));
}
