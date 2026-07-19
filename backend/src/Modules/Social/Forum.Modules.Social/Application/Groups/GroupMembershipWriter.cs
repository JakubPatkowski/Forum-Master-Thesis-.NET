using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Domain.Conversations;
using Forum.Modules.Social.Domain.Groups;

namespace Forum.Modules.Social.Application.Groups;

/// <summary>
/// The one place membership and the chat seat change TOGETHER (join/accept-invite/leave/kick all route here), so
/// "participant row = may read/send" can never drift from "membership row = is in the group".
/// </summary>
internal static class GroupMembershipWriter
{
    public static async Task AddMemberAsync(
        IGroupRepository groups,
        IConversationRepository conversations,
        Ulid groupId,
        Ulid userId,
        Ulid? invitedBy,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        groups.AddMembership(new GroupMembership(groupId, userId, now, invitedBy));

        var seat = await conversations.GetParticipantAsync(groupId, userId, cancellationToken);
        if (seat is null)
        {
            conversations.AddParticipant(new ConversationParticipant(groupId, userId, now));
        }
        else
        {
            seat.Rejoin(now);
        }
    }

    public static async Task RemoveMemberAsync(
        IGroupRepository groups,
        IConversationRepository conversations,
        GroupMembership membership,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        groups.RemoveMembership(membership);
        var seat = await conversations.GetParticipantAsync(membership.GroupId, membership.UserId, cancellationToken);
        seat?.Leave(now);
    }
}
