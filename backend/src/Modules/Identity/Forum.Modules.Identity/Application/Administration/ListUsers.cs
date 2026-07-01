using Forum.Common.Cqrs;
using Forum.Common.Paging;
using Forum.Modules.Identity.Application.Abstractions;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Identity.Application.Administration;

/// <summary>Keyset-paged list of users for the admin surface.</summary>
internal sealed record ListUsersQuery(string? Cursor, int Limit) : IQuery<CursorPage<UserSummaryResponse>>;

internal sealed class ListUsersQueryHandler : IQueryHandler<ListUsersQuery, CursorPage<UserSummaryResponse>>
{
    private const int MaxLimit = 100;
    private const int DefaultLimit = 20;

    private readonly IUserQueries _users;

    public ListUsersQueryHandler(IUserQueries users) => _users = users;

    public async Task<Result<CursorPage<UserSummaryResponse>>> Handle(ListUsersQuery query, CancellationToken cancellationToken)
    {
        var limit = query.Limit is <= 0 ? DefaultLimit : Math.Min(query.Limit, MaxLimit);
        var page = await _users.ListAsync(query.Cursor, limit, cancellationToken);
        return Result.Success(page);
    }
}
