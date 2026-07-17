using Forum.Common.Cqrs;
using Forum.Common.Paging;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.SharedKernel.Results;

using Microsoft.Extensions.Options;

namespace Forum.Modules.Social.Application.Groups;

/// <summary>
/// The member list — members-only for public AND private groups (who's inside is a member fact, like the chat;
/// visibility only governs discovery). Staff passing the manage-gate can read it too (acting on reports needs
/// the roster). Keyset by user id.
/// </summary>
internal sealed record GetGroupMembersQuery(Ulid GroupId, string? Cursor, int? Limit)
    : IQuery<CursorPage<GroupMemberResponse>>;

internal sealed class GetGroupMembersQueryHandler
    : IQueryHandler<GetGroupMembersQuery, CursorPage<GroupMemberResponse>>
{
    private readonly ISocialQueries _queries;
    private readonly IGroupRepository _groups;
    private readonly ICurrentUser _currentUser;
    private readonly SocialOptions _options;

    public GetGroupMembersQueryHandler(
        ISocialQueries queries, IGroupRepository groups, ICurrentUser currentUser, IOptions<SocialOptions> options)
    {
        _queries = queries;
        _groups = groups;
        _currentUser = currentUser;
        _options = options.Value;
    }

    public async Task<Result<CursorPage<GroupMemberResponse>>> Handle(
        GetGroupMembersQuery query, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure<CursorPage<GroupMemberResponse>>(SocialErrors.AuthenticationRequired);
        }

        var group = await _groups.GetByIdAsync(query.GroupId, cancellationToken);
        if (group is null)
        {
            return Result.Failure<CursorPage<GroupMemberResponse>>(SocialErrors.GroupNotFound);
        }

        if (await _groups.GetMembershipAsync(group.Id, userId, cancellationToken) is null
            && !await GroupGuards.MayManageAsync(_currentUser, group, cancellationToken))
        {
            return Result.Failure<CursorPage<GroupMemberResponse>>(SocialErrors.NotGroupMember);
        }

        if (!SocialCursors.TryParse(query.Cursor, out var cursor))
        {
            return Result.Failure<CursorPage<GroupMemberResponse>>(SocialErrors.InvalidCursor);
        }

        var limit = SocialCursors.ClampLimit(query.Limit, _options);
        return Result.Success(await _queries.GetGroupMembersAsync(group.Id, cursor, limit, cancellationToken));
    }
}
