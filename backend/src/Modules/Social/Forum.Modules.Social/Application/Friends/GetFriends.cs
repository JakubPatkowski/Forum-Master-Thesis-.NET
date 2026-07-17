using Forum.Common.Cqrs;
using Forum.Common.Paging;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.SharedKernel.Results;

using Microsoft.Extensions.Options;

namespace Forum.Modules.Social.Application.Friends;

/// <summary>The caller's accepted friends, newest friendship first (keyset by friendship id).</summary>
internal sealed record GetFriendsQuery(string? Cursor, int? Limit) : IQuery<CursorPage<FriendResponse>>;

internal sealed class GetFriendsQueryHandler : IQueryHandler<GetFriendsQuery, CursorPage<FriendResponse>>
{
    private readonly ISocialQueries _queries;
    private readonly ICurrentUser _currentUser;
    private readonly SocialOptions _options;

    public GetFriendsQueryHandler(ISocialQueries queries, ICurrentUser currentUser, IOptions<SocialOptions> options)
    {
        _queries = queries;
        _currentUser = currentUser;
        _options = options.Value;
    }

    public async Task<Result<CursorPage<FriendResponse>>> Handle(
        GetFriendsQuery query, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure<CursorPage<FriendResponse>>(SocialErrors.AuthenticationRequired);
        }

        if (!SocialCursors.TryParse(query.Cursor, out var cursor))
        {
            return Result.Failure<CursorPage<FriendResponse>>(SocialErrors.InvalidCursor);
        }

        var limit = SocialCursors.ClampLimit(query.Limit, _options);
        return Result.Success(await _queries.GetFriendsAsync(userId, cursor, limit, cancellationToken));
    }
}
