using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Friends;

/// <summary>The caller's pending requests, both directions in one response (each side is naturally small).</summary>
internal sealed record GetFriendRequestsQuery : IQuery<FriendRequestsResponse>;

internal sealed class GetFriendRequestsQueryHandler : IQueryHandler<GetFriendRequestsQuery, FriendRequestsResponse>
{
    private readonly ISocialQueries _queries;
    private readonly ICurrentUser _currentUser;

    public GetFriendRequestsQueryHandler(ISocialQueries queries, ICurrentUser currentUser)
    {
        _queries = queries;
        _currentUser = currentUser;
    }

    public async Task<Result<FriendRequestsResponse>> Handle(
        GetFriendRequestsQuery query, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure<FriendRequestsResponse>(SocialErrors.AuthenticationRequired);
        }

        return Result.Success(await _queries.GetFriendRequestsAsync(userId, cancellationToken));
    }
}
