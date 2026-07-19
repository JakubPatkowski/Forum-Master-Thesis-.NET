using Forum.Common.Cqrs;
using Forum.Common.Paging;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.SharedKernel.Results;

using Microsoft.Extensions.Options;

namespace Forum.Modules.Social.Application.Groups;

/// <summary>Group directory: public groups plus the viewer's memberships (or one of the two, by filter).</summary>
internal sealed record GetGroupsQuery(string? Filter, string? Cursor, int? Limit)
    : IQuery<CursorPage<GroupSummaryResponse>>;

internal sealed class GetGroupsQueryHandler : IQueryHandler<GetGroupsQuery, CursorPage<GroupSummaryResponse>>
{
    private readonly ISocialQueries _queries;
    private readonly ICurrentUser _currentUser;
    private readonly SocialOptions _options;

    public GetGroupsQueryHandler(ISocialQueries queries, ICurrentUser currentUser, IOptions<SocialOptions> options)
    {
        _queries = queries;
        _currentUser = currentUser;
        _options = options.Value;
    }

    public async Task<Result<CursorPage<GroupSummaryResponse>>> Handle(
        GetGroupsQuery query, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure<CursorPage<GroupSummaryResponse>>(SocialErrors.AuthenticationRequired);
        }

        var filter = query.Filter?.Trim().ToLowerInvariant() switch
        {
            null or "" or "all" => GroupListFilter.All,
            "mine" => GroupListFilter.Mine,
            "public" => GroupListFilter.Public,
            _ => (GroupListFilter?)null,
        };
        if (filter is null)
        {
            return Result.Failure<CursorPage<GroupSummaryResponse>>(
                Error.Validation("Social.UnknownFilter", "Filter must be 'all', 'mine' or 'public'."));
        }

        if (!SocialCursors.TryParse(query.Cursor, out var cursor))
        {
            return Result.Failure<CursorPage<GroupSummaryResponse>>(SocialErrors.InvalidCursor);
        }

        var limit = SocialCursors.ClampLimit(query.Limit, _options);
        return Result.Success(
            await _queries.GetGroupsAsync(userId, filter.Value, cursor, limit, cancellationToken));
    }
}
