using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Groups;

/// <summary>The caller's pending group invites (naturally small; no cursor).</summary>
internal sealed record GetMyInvitesQuery : IQuery<IReadOnlyList<GroupInviteResponse>>;

internal sealed class GetMyInvitesQueryHandler : IQueryHandler<GetMyInvitesQuery, IReadOnlyList<GroupInviteResponse>>
{
    private readonly ISocialQueries _queries;
    private readonly ICurrentUser _currentUser;

    public GetMyInvitesQueryHandler(ISocialQueries queries, ICurrentUser currentUser)
    {
        _queries = queries;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyList<GroupInviteResponse>>> Handle(
        GetMyInvitesQuery query, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure<IReadOnlyList<GroupInviteResponse>>(SocialErrors.AuthenticationRequired);
        }

        return Result.Success(await _queries.GetMyInvitesAsync(userId, cancellationToken));
    }
}
