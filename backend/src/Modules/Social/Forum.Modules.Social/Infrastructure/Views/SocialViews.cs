namespace Forum.Modules.Social.Infrastructure.Views;

/// <summary>
/// The raw SQL for the module's read views (applied by the <c>AddSocialViews</c> migration; kept out of the EF
/// model like Content's FTS/views and Engagement's counters). Views JOIN <c>forum_identity.users</c> and read
/// <c>forum_authz.acl_entries</c> — the sanctioned VIEW-LEVEL read joins (Identity migrates first; no schema-level
/// FK ever crosses modules). The group-admin flag resolves the <c>moderate</c> bit from the action catalog instead
/// of hardcoding 64, so the views can never drift from the mask layout.
/// </summary>
internal static class SocialViews
{
    public const string Up =
        """
        -- Accepted friendships, expanded to one row per direction (each side lists the OTHER as the friend).
        CREATE VIEW forum_social.friend_list_v AS
        SELECT f.id AS friendship_id,
               side.user_id,
               side.friend_id,
               u.username AS friend_username,
               COALESCE(f.accepted_on_utc, f.created_on_utc) AS friends_since_utc
        FROM forum_social.friendships f
        CROSS JOIN LATERAL (VALUES (f.requester_id, f.addressee_id), (f.addressee_id, f.requester_id))
            AS side(user_id, friend_id)
        JOIN forum_identity.users u ON u.id = side.friend_id
        WHERE f.status = 'accepted';

        -- Pending requests with both usernames (the caller filters by their side).
        CREATE VIEW forum_social.friend_request_v AS
        SELECT f.id AS friendship_id,
               f.requester_id,
               ru.username AS requester_username,
               f.addressee_id,
               au.username AS addressee_username,
               f.created_on_utc
        FROM forum_social.friendships f
        JOIN forum_identity.users ru ON ru.id = f.requester_id
        JOIN forum_identity.users au ON au.id = f.addressee_id
        WHERE f.status = 'pending';

        CREATE VIEW forum_social.blocked_list_v AS
        SELECT b.blocker_id, b.blocked_id, u.username AS blocked_username, b.created_on_utc
        FROM forum_social.social_blocks b
        JOIN forum_identity.users u ON u.id = b.blocked_id;

        -- Live groups with owner + member count; the viewer's own membership is an EXISTS in the query.
        CREATE VIEW forum_social.group_list_v AS
        SELECT g.id AS group_id,
               g.name,
               g.description,
               g.visibility,
               g.owner_id,
               ou.username AS owner_username,
               (SELECT count(*)::int FROM forum_social.group_memberships m WHERE m.group_id = g.id) AS member_count,
               g.created_on_utc
        FROM forum_social.groups g
        JOIN forum_identity.users ou ON ou.id = g.owner_id
        WHERE g.is_deleted = false;

        -- Members with the admin flag resolved live from the ACL (owner is implicitly admin).
        CREATE VIEW forum_social.group_member_v AS
        SELECT m.group_id,
               m.user_id,
               u.username,
               m.joined_on_utc,
               (g.owner_id = m.user_id) AS is_owner,
               ((g.owner_id = m.user_id) OR EXISTS (
                   SELECT 1 FROM forum_authz.acl_entries a
                   WHERE a.scope = 'group' AND a.scope_id = m.group_id::text
                     AND a.principal_type = 'user' AND a.principal_id = m.user_id::text
                     AND (a.allow_bits
                          & (1 << (SELECT bit FROM forum_authz.actions WHERE code = 'moderate'))) <> 0
               )) AS is_admin
        FROM forum_social.group_memberships m
        JOIN forum_social.groups g ON g.id = m.group_id AND g.is_deleted = false
        JOIN forum_identity.users u ON u.id = m.user_id;

        -- Pending invites with group + both usernames; invites into deleted groups vanish with the join.
        CREATE VIEW forum_social.group_invite_v AS
        SELECT i.id AS invite_id,
               i.group_id,
               g.name AS group_name,
               i.invited_user_id,
               iu.username AS invited_username,
               i.invited_by,
               bu.username AS invited_by_username,
               i.created_on_utc
        FROM forum_social.group_invites i
        JOIN forum_social.groups g ON g.id = i.group_id AND g.is_deleted = false
        JOIN forum_identity.users iu ON iu.id = i.invited_user_id
        JOIN forum_identity.users bu ON bu.id = i.invited_by;

        -- One row per ACTIVE seat: display name, last-message preview and the seat owner's unread count.
        CREATE VIEW forum_social.conversation_list_v AS
        SELECT p.user_id,
               c.id AS conversation_id,
               c.type,
               CASE WHEN c.type = 'group' THEN g.name ELSE ou.username END AS display_name,
               CASE WHEN c.type = 'direct' THEN op.user_id END AS other_user_id,
               CASE WHEN c.type = 'group' THEN c.id END AS group_id,
               lm.id AS last_message_id,
               CASE WHEN lm.is_deleted THEN '[deleted]' ELSE left(lm.body, 120) END AS last_message_preview,
               lm.owner_id AS last_message_sender_id,
               lm.created_on_utc AS last_message_on_utc,
               (SELECT count(*)::int FROM forum_social.messages m2
                 WHERE m2.conversation_id = c.id
                   AND m2.owner_id <> p.user_id
                   AND (p.last_read_on_utc IS NULL OR m2.created_on_utc > p.last_read_on_utc)) AS unread_count,
               p.is_muted
        FROM forum_social.conversation_participants p
        JOIN forum_social.conversations c ON c.id = p.conversation_id
        LEFT JOIN forum_social.groups g ON g.id = c.id AND c.type = 'group'
        LEFT JOIN LATERAL (
            SELECT p2.user_id FROM forum_social.conversation_participants p2
            WHERE p2.conversation_id = c.id AND p2.user_id <> p.user_id
            LIMIT 1
        ) op ON c.type = 'direct'
        LEFT JOIN forum_identity.users ou ON ou.id = op.user_id
        LEFT JOIN LATERAL (
            SELECT m.id, m.body, m.owner_id, m.created_on_utc, m.is_deleted
            FROM forum_social.messages m
            WHERE m.conversation_id = c.id
            ORDER BY m.id DESC
            LIMIT 1
        ) lm ON TRUE
        WHERE p.left_on_utc IS NULL
          AND (c.type = 'direct' OR g.id IS NOT NULL);

        -- History keeps tombstones (masked body), exactly like comment_tree_v keeps deleted comments.
        CREATE VIEW forum_social.message_history_v AS
        SELECT m.id AS message_id,
               m.conversation_id,
               m.owner_id AS sender_id,
               u.username AS sender_username,
               CASE WHEN m.is_deleted THEN '[deleted]' ELSE m.body END AS body,
               m.created_on_utc,
               m.edited_on_utc,
               m.is_deleted
        FROM forum_social.messages m
        JOIN forum_identity.users u ON u.id = m.owner_id;

        CREATE VIEW forum_social.notification_list_v AS
        SELECT n.id AS notification_id,
               n.user_id,
               n.kind,
               n.actor_id,
               au.username AS actor_username,
               n.target_id,
               n.is_read,
               n.created_on_utc
        FROM forum_social.notifications n
        LEFT JOIN forum_identity.users au ON au.id = n.actor_id;
        """;

    public const string Down =
        """
        DROP VIEW IF EXISTS forum_social.notification_list_v;
        DROP VIEW IF EXISTS forum_social.message_history_v;
        DROP VIEW IF EXISTS forum_social.conversation_list_v;
        DROP VIEW IF EXISTS forum_social.group_invite_v;
        DROP VIEW IF EXISTS forum_social.group_member_v;
        DROP VIEW IF EXISTS forum_social.group_list_v;
        DROP VIEW IF EXISTS forum_social.blocked_list_v;
        DROP VIEW IF EXISTS forum_social.friend_request_v;
        DROP VIEW IF EXISTS forum_social.friend_list_v;
        """;
}
