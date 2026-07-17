using System.Text.Json;
using System.Text.Json.Serialization;

namespace Forum.Api.Realtime;

/// <summary>
/// The compact change notification pushed to subscribed clients (ADR 0010): identity + routing, never entity
/// fields — the client re-fetches whatever the notification touches, so nothing a viewer must not see can leak.
/// <para><c>ParentId</c> is the containing aggregate used for routing: the thread for comments and reactions,
/// absent for threads (their category is already in <c>CategoryId</c>). For <c>entity = "reaction"</c>,
/// <c>Id</c> is the reacted target (thread or comment) whose like count changed — the client already knows that
/// id from its rendered view. ADR 0010's illustrative <c>version</c> field is deliberately omitted: no aggregate
/// carries an optimistic-concurrency token, and a made-up counter would invite the client to trust an ordering
/// the at-least-once bus cannot guarantee; freshness comes from the re-fetch.</para>
/// </summary>
internal sealed record ChangeNotification(string Type, string Entity, string Id, string? ParentId, string? CategoryId)
{
    public const string Created = "created";
    public const string Updated = "updated";
    public const string Deleted = "deleted";

    public const string ThreadEntity = "thread";
    public const string CommentEntity = "comment";
    public const string ReactionEntity = "reaction";

    // Social entities (Phase 11). CategoryId stays null for these; ParentId is the container:
    // the conversation for messages, the group for invites/members, absent for friendships/notifications.
    public const string FriendshipEntity = "friendship";
    public const string GroupEntity = "group";
    public const string GroupMemberEntity = "group_member";
    public const string GroupInviteEntity = "group_invite";
    public const string MessageEntity = "message";
    public const string NotificationEntity = "notification";
}

/// <summary>The single wire format of the hub: camelCase, nulls omitted — both directions use it.</summary>
internal static class RealtimeJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
