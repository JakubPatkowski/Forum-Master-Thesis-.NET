using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Groups;

/// <summary>
/// Group detail with viewer facts. A private group is 403 for non-members (mirror of Content's private-category
/// gate: its existence is knowable, its facts are not) unless the viewer passes the manage-gate.
/// </summary>
internal sealed record GetGroupQuery(Ulid GroupId) : IQuery<GroupDetailResponse>;

internal sealed class GetGroupQueryHandler : IQueryHandler<GetGroupQuery, GroupDetailResponse>
{
    private readonly ISocialQueries _queries;
    private readonly ICurrentUser _currentUser;

    public GetGroupQueryHandler(ISocialQueries queries, ICurrentUser currentUser)
    {
        _queries = queries;
        _currentUser = currentUser;
    }

    public async Task<Result<GroupDetailResponse>> Handle(GetGroupQuery query, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure<GroupDetailResponse>(SocialErrors.AuthenticationRequired);
        }

        var group = await _queries.GetGroupAsync(query.GroupId, userId, cancellationToken);
        if (group is null)
        {
            return Result.Failure<GroupDetailResponse>(SocialErrors.GroupNotFound);
        }

        if (group.Visibility == "private" && group is { IsMember: false, IsAdmin: false }
            && !_currentUser.IsOwner(group.OwnerId)
            && !await _currentUser.HasPermissionAsync(
                Permissions.Moderate, PermissionScopes.Group, group.GroupId, cancellationToken))
        {
            return Result.Failure<GroupDetailResponse>(SocialErrors.GroupPrivate);
        }

        return Result.Success(group);
    }
}
