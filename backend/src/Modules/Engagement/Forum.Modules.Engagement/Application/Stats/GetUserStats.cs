using Forum.Common.Cqrs;
using Forum.Modules.Engagement.Application.Abstractions;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Engagement.Application.Stats;

/// <summary>Reads a user's public stats row from <c>user_stats_v</c> (anonymous allowed; profile parity).</summary>
internal sealed record GetUserStatsQuery(Ulid UserId) : IQuery<UserStatsResponse>;

internal sealed class GetUserStatsQueryHandler : IQueryHandler<GetUserStatsQuery, UserStatsResponse>
{
    private readonly IEngagementQueries _queries;

    public GetUserStatsQueryHandler(IEngagementQueries queries) => _queries = queries;

    public async Task<Result<UserStatsResponse>> Handle(GetUserStatsQuery query, CancellationToken cancellationToken)
    {
        var stats = await _queries.GetUserStatsAsync(query.UserId, cancellationToken);
        return stats is null
            ? Result.Failure<UserStatsResponse>(EngagementErrors.UserNotFound)
            : Result.Success(stats);
    }
}
