using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Blocks;

/// <summary>The caller's own block list (only ever visible to its owner).</summary>
internal sealed record GetBlockedUsersQuery : IQuery<IReadOnlyList<BlockedUserResponse>>;

internal sealed class GetBlockedUsersQueryHandler
    : IQueryHandler<GetBlockedUsersQuery, IReadOnlyList<BlockedUserResponse>>
{
    private readonly ISocialQueries _queries;
    private readonly ICurrentUser _currentUser;

    public GetBlockedUsersQueryHandler(ISocialQueries queries, ICurrentUser currentUser)
    {
        _queries = queries;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<BlockedUserResponse>>> Handle(
        GetBlockedUsersQuery query, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure<IReadOnlyList<BlockedUserResponse>>(SocialErrors.AuthenticationRequired);
        }

        return Result.Success(await _queries.GetBlockedUsersAsync(userId, cancellationToken));
    }
}
