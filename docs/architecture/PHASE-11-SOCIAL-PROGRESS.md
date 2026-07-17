# Phase 11 (plan Phase 5) — Social module: WORKING PROGRESS + LOCKED DESIGN

_Written 2026-07-16 by the Fable 5 backend session (branch `27-feat-phase-11---social`). This file is the
session-continuity anchor: it records every locked design decision and the live implementation checklist.
If the session was interrupted, resume from the checklist at the bottom — the design here is FINAL unless a
compile/test error forces a deviation (record any deviation in place). Delete this file once CLAUDE.md's
Phase 5 entry is written and everything is green._

Brief source: `docs/architecture/SOCIAL-MODULE-FABLE5-PROMPTS.md` Part A. Scope: friends, social blocks,
groups (+roles via ACL), unified DM/group-chat messaging, durable notifications, privacy settings,
presence (A-only, NOT in B-parity seed/k6 numbers).

## Locked design decisions (with rationale)

1. **Schema `forum_social`**, module `Forum.Modules.Social`, ordinary EF migrations (`InitialSocial`) +
   one raw-SQL migration (`AddSocialViews`) for read views, mirroring Content/Engagement precedent.

2. **ACL: ZERO new action bits.** Catalog uses bits 0–7 of 32 (`int`), 24 free — but group-admin is
   expressed with the EXISTING `moderate` (bit 6, value 64) at the new `PermissionScopes.Group = "group"`
   scope. Posting/reading group chat is NOT ACL-gated at all — membership (module-owned fact) is the gate.
   Consequence (deliberate): global moderators/admins hold `moderate` at every scope via role templates →
   they can MANAGE any group (rename/kick/delete — platform staff acting on reports) but do NOT implicitly
   read group chat (membership gate). DMs are never staff-readable (participant gate only).

3. **ACL writes from Social: new shared port `IAclGrantService`** in `Forum.Common/Security` (next to
   `IPermissionService`), implemented in Identity (`AclGrantService` wrapping the existing
   `IAuthorizationStore` upsert + synchronous `recompute_user_perms`). Social calls it when
   promoting/demoting a group member (grant/revoke `moderate`@group:id). Rationale: Social must not write
   `forum_authz` (cross-schema), async events would open a revocation window (Phase 6 kept authz recompute
   synchronous deliberately), and `IPermissionService` already set the precedent of a shared authz surface
   registered by Identity. Grant is idempotent; revoke removes the entry and recomputes.
   NOTE: `AddUserAclEntryAsync` is a bare INSERT — `AclGrantService` must implement upsert semantics
   (delete existing group-scope row for the user first, or INSERT ... ON CONFLICT via its own SQL).

4. **Domain model** (all ULID, snake_case, audit via AggregateRoot unless noted):
   - `Friendship` (Id PK; RequesterId, AddresseeId, Status: Pending/Accepted) — AggregateRoot. Decline and
     cancel DELETE the row (no Declined tombstone: requester may retry later; spam is bounded by privacy
     setting + blocks). Remove friend deletes too. Uniqueness: EF unique index on (requester_id,
     addressee_id) + raw-SQL unique expression index on (LEAST(requester_id, addressee_id),
     GREATEST(...)) added via `migrationBuilder.Sql` in InitialSocial — closes the A→B/B→A race.
   - `SocialBlock` (BlockerId+BlockedId composite PK, CreatedOnUtc) — plain entity (Reaction precedent),
     deliberately NOT named anything with "UserBlock" to avoid colliding with Identity's admin-ban
     `UserStatus.Blocked`/`UserBlockedIntegrationEvent`. Creating a block also deletes (same tx): any
     friendship (either direction), pending friend requests, pending group invites between the pair.
     Asymmetric: A blocks B ⇒ B cannot friend-request/DM/invite A; A also cannot re-friend B until unblock
     (friendship row is gone), but A MAY still message B?? — NO: block suppresses DMs in BOTH directions
     (a blocker sending to their blocked party is nonsense UX); document in handler.
   - `Group` (Id PK, Name, Description, Visibility Public/Private, OwnerId (IOwned), ISoftDeletable,
     audit) — AggregateRoot. NO icon_file_id column (icons ride Files' by-target read, like avatars today).
     Public = discoverable + joinable without invite; Private = invite-only. Chat/membership facts are
     member-only in BOTH cases (visibility affects discovery/join, never chat reads).
   - `GroupMembership` (GroupId+UserId composite PK, JoinedOnUtc, InvitedBy nullable) — plain entity.
     THE membership fact. Owner also has a membership row.
   - `GroupInvite` (Id PK, GroupId, InvitedUserId, InvitedBy, Status Pending only — accepted/declined rows
     are DELETED like friend requests) — AggregateRoot (audit). Partial unique index (group_id,
     invited_user_id) [status column dropped → plain unique index, since non-pending rows don't persist].
   - `Conversation` (Id PK, Type Direct/Group, DirectKey nullable text) — AggregateRoot. For a group chat
     the conversation Id == the Group Id (Type=Group), created in the same tx as the group — no nullable FK
     indirection. For DMs: get-or-create on chat open (`POST /conversations/direct`), race-safe via
     `DirectKey = "{loUlid}:{hiUlid}"` with partial unique index WHERE direct_key IS NOT NULL.
   - `ConversationParticipant` (ConversationId+UserId composite PK, JoinedOnUtc, LeftOnUtc nullable,
     LastReadOnUtc nullable, IsMuted) — plain entity. THE single authorization fact for every message
     read/send regardless of type; group membership changes write through to it in the same tx
     (join → add/re-activate row, leave/kick → set LeftOnUtc). LastReadOnUtc drives the owner's own unread
     badge ONLY (read-receipts-to-sender are out of scope per REQUIREMENTS §1 — do not build).
   - `Message` (Id PK, ConversationId, SenderId, Body markdown, ISoftDeletable, audit) — AggregateRoot.
     Delete = tombstone: body → "[deleted]" + IsDeleted, row kept (Comment.Delete precedent); history view
     returns deleted rows with masked body. Edit updates Body (LastModified = edited marker). Keyset:
     ix_messages_history (conversation_id, id DESC) — ULID id embeds creation time, cursor = last id.
   - `Notification` (Id PK, UserId, Kind text, ActorId nullable, TargetId nullable, IsRead,
     CreatedOnUtc) — plain entity (no audit ceremony; system-generated). Kinds (closed set):
     `friend.request`, `friend.accepted`, `group.invite`, `group.invite.accepted`, `group.kicked`.
     Message arrivals do NOT create notification rows (unread badge comes from LastReadOnUtc; a
     per-message durable row would explode). Index (user_id, id DESC) + partial WHERE NOT is_read.
   - `UserPrivacySettings` (UserId PK, FriendRequests everyone|no_one, Messages everyone|friends|no_one,
     GroupInvites everyone|friends|no_one, ShowOnlineStatus bool) — plain entity, lazily created
     (absent row = defaults: everyone/everyone/everyone/true). Enforced in send-friend-request,
     open-direct-conversation/send-message, invite handlers, 404→403→422 order.
   - `UserPresence` (UserId PK, LastHeartbeatOnUtc) — plain entity behind `IPresenceStore` port
     (Application.Abstractions): Heartbeat (upsert ON CONFLICT), GetStatusesAsync batch (one round trip,
     Engagement-batch precedent). Online < 60 s, Away < 300 s, else Offline (SocialOptions). ShowOnlineStatus
     = false reads as Offline for everyone else. Heartbeat trigger: REST `POST /api/social/presence/heartbeat`
     on a ~30 s SPA interval — chosen over WS-lifecycle derivation because it needs zero Bootstrap coupling,
     survives socket flaps, and is the exact seam the scoped Redis session swaps
     (config-gated Postgres|Redis store, §10d ticket-cache idiom). NO realtime presence pushes (poll the
     batch endpoint) — presence is A-only and unmeasured; keep it off the bus.

5. **Realtime hub generalization (Bootstrap edits — expected, not a violation):**
   - `RealtimeNotification` reshaped to `(ChangeNotification Payload, RealtimeVisibility Visibility,
     IReadOnlyList<SubscriptionView> Routes)` where `RealtimeVisibility` = record(Kind:
     Category|Conversation|TargetUsers, Id). Matching = subscription ∩ Routes (SubscriptionSet.MatchesAny);
     the old (CategoryId, ThreadId, ActorUserId) triple is now just the Routes list a mapper builds.
     Rationale: a second module made the Content-specific field names a wart (the brief invited this
     refactor); routes-as-data removes per-kind match logic from SubscriptionSet entirely.
   - `ViewKind` += `Group`, `Conversation` (parse "group"/"conversation"). Subscribe-time: both follow the
     category precedent (always accept, authorize per push) — group membership changes mid-connection, and
     one code path beats two; `user` stays self-only at subscribe.
   - Dispatcher visibility step branches on Visibility.Kind: Category → `IContentVisibility` (unchanged
     owner-or-moderate), Conversation → NEW `ISocialVisibility.IsConversationParticipantAsync(convId,
     userId)` per subscriber (covers groups too: group conversation id == group id and membership writes
     through to participants — ONE port method), TargetUsers → no check (user views are subscribe-time
     self-gated; routes only contain user views). Group-scoped events (rename/member changes) use
     Conversation visibility with the group's conversation id.
   - `ChangeNotification` envelope UNCHANGED (frontend contract): social events set CategoryId = null and
     use ParentId as container (conversation id for messages, group id for invites/members). New entities:
     `friendship`, `group`, `group_invite`, `group_member`, `message`, `notification`.
   - Event → route map (RealtimeEventMap additions):
     - FriendRequestSent/Accepted/Declined/FriendRemoved → TargetUsers, routes [user:requester, user:addressee].
     - GroupUpdated/Deleted, GroupMemberJoined/Left → Conversation(groupId), routes [group:id]
       (+ [user:removedUser] TargetUsers? NO — kicked user gets the bell via NotificationCreated).
     - GroupInviteSent/Responded → TargetUsers, routes [user:invitee, user:inviter].
     - MessageSent/Edited/Deleted → Conversation(convId); routes [conversation:convId] + for DIRECT also
       [user:participantA, user:participantB] (badge path; events carry the two participant ids), for GROUP
       also [group:convId] (members on the group page; no per-member fan-out — documented tradeoff: group
       badge freshness comes from open social views or next resync, DMs always hit the user view).
     - NotificationCreated → TargetUsers, routes [user:recipient].
     - GroupCreated deliberately NOT wired (creator gets the response; nobody else can see it yet).
   - `Program.cs`: `new SocialModule()` appended (after Engagement; migration/registration order matters —
     views JOIN forum_identity.users).

6. **Integration events** (`Forum.Modules.Social.Contracts.IntegrationEvents.*` → exchange `social` derived
   from namespace): FriendRequestSent/Accepted/Declined, FriendRemoved, GroupCreated/Updated/Deleted,
   GroupInviteSent/Responded, GroupMemberJoined/Left, MessageSent/Edited/Deleted (carry ConversationId,
   ConversationType wire string, DirectParticipantIds for DMs), NotificationCreated. All written via
   module-local `IOutboxWriter` (Engagement copy).

7. **Consumers**: Social consumes Identity's `UserBlockedIntegrationEvent` (admin ban ≠ SocialBlock — this
   is the additive third consumer): deletes the banned account's pending friend requests (both directions)
   and pending group invites. Files consumes Social's `MessageDeleted` (detach message images) and
   `GroupDeleted` (detach group icon) — mirrors ThreadDeleted/CommentDeleted consumers.

8. **Files integration** (direct edits to Files — Files → Social.Contracts project ref, boundary test
   extended): enum member `Dm` REPURPOSED → `Message` (wire "message"; no live rows carry 'dm' — it was
   422-rejected), new `GroupIcon` (wire "group_icon"). New port `ISocialAuthorization` in Social.Contracts:
   `AuthorizeAttachmentAsync(SocialAttachmentTarget target, Ulid targetId, Ulid userId, ct)` → Result
   (Message: exists 404 → sender-only 403; GroupIcon: exists 404 → owner-or-moderate@group 403) AND
   `AuthorizeFileReadAsync(SocialAttachmentTarget, Ulid targetId, Ulid? userId, ct)` → Result — the READ
   gap is CLOSED for message images (participant-only; anonymous → 403 with 404-shaped ordering handled by
   Social), group icons stay anonymous-readable (avatar parity). GetFileDownload/ListTargetFiles gate ONLY
   when the file's attachment target is Message. GroupIcon joins the replace-semantics arm; Message is
   additive under the existing global `MaxAttachmentsPerTarget` cap (no per-target override — 10 images per
   message is already generous; recorded as deliberate).

9. **Reads**: raw-SQL views (`AddSocialViews`) + ADO `SocialQueries` (Content/Engagement pattern), keyset
   everywhere, no OFFSET: `friend_list_v` (accepted, expanded both directions, joins users for username),
   `friend_request_v` (pending + usernames both sides), `group_list_v` (+member_count, owner_username),
   `group_member_v` (+username, is_admin = owner OR acl moderate bit at group scope — cross-schema read
   join into forum_authz, view-level precedent), `conversation_list_v` (participant rows + last-message
   lateral + unread count + display name: other user for direct / group name for group),
   `message_history_v` (sender username, body masked when deleted). Notifications read via plain ADO on
   the table (single-table, no view needed) — keyset (user_id, id DESC).

10. **Endpoints** (~28 files, `/api/social/*`, tag "Social", RequireAuthorization except none — ALL social
    endpoints are authenticated; there is no anonymous social surface):
    Friends: POST /friends/requests {addresseeId}; POST /friends/requests/{id}/accept;
    DELETE /friends/requests/{id} (decline by addressee / cancel by requester); DELETE /friends/{userId};
    GET /friends; GET /friends/requests (incoming+outgoing).
    Blocks: PUT /blocks/{userId} (idempotent); DELETE /blocks/{userId}; GET /blocks.
    Groups: POST /groups; GET /groups?filter=mine|public&cursor=; GET /groups/{id}; PUT /groups/{id};
    DELETE /groups/{id}; GET /groups/{id}/members?cursor=; DELETE /groups/{id}/members/{userId} (kick/leave
    when self); POST /groups/{id}/join (public only); PUT /groups/{id}/members/{userId}/role {role:
    admin|member} (owner/moderate@group; grants/revokes ACL via IAclGrantService);
    POST /groups/{id}/invites {userId}; POST /invites/{id}/accept; DELETE /invites/{id} (decline/cancel);
    GET /invites (mine, pending).
    Ownership rule (tested): the owner cannot leave or be kicked; owner may transfer via PUT
    /groups/{id}/owner {userId: member} or delete the group. Sole-owner leave → 422.
    Messaging: POST /conversations/direct {userId} (get-or-create; privacy+block gates);
    GET /conversations; GET /conversations/{id}/messages?cursor=; POST /conversations/{id}/messages {body};
    PUT /messages/{id}; DELETE /messages/{id} (sender, or owner/moderate@group for group chats);
    POST /conversations/{id}/read (stamps LastReadOnUtc = now).
    Notifications: GET /notifications?cursor=&unreadOnly=; POST /notifications/read {ids?} (absent = all);
    GET /notifications/unread-count.
    Privacy: GET /privacy; PUT /privacy.
    Presence: POST /presence/heartbeat; GET /presence?userIds= (≤100, batch).

11. **Seeding**: `SocialSeeder` (Order 4). SeedStreams += friendship/group/group-invite/conversation/
    message/notification (+SeedTime offsets/steps). Development counts: 4 accepted friendships + 1 pending,
    2 groups (public "book-club" by alice, private "staff-room" by mod, 3 members each), 1 pending invite,
    1 direct conversation (alice↔bob) + the 2 group conversations, 12 messages, 3 notifications, privacy
    defaults, no presence. Group-admin ACLs seeded via IAclGrantService calls (tiny counts). Benchmark
    counts = 0 (BLOCKED on Hubert — POST-9C-ROADMAP Decision 3; TODO marked in SeedPlan).

12. **Tests**: `Forum.Modules.Social.Tests` (unit: friendship transitions, group owner-leave invariant,
    message tombstone, privacy gates, presence staleness, block effects, 404→403→422 ordering on key
    handlers); ArchitectureTests += Social boundary (Social touches Identity/Content? — Social needs NO
    Content ref; Identity via Contracts only) + Files→Social.Contracts allowed + nobody depends on Social
    except Files + domain purity; IntegrationTests `SocialFlowTests` (friend→accept→DM→WS push; group
    create→invite→accept→message→push to members; block prevents request+DM; privacy 403; icon replace +
    message image attach/detach on delete; presence online→offline) + Api.Tests for new
    SubscriptionSet/EventMap arms.

13. **Docs at the end**: CLAUDE.md Phase 5 entry + Next paragraph; DOMAIN-MODEL-AND-DATABASE.md §6 replace;
    permissions-acl-design.md group scope + "0 new bits (8/32 used)"; REQUIREMENTS-AND-ASSUMPTIONS.md scope
    update; ADR 0011 (realtime hub generalization: routes-as-data + per-kind visibility) — yes, ADR-worthy
    (Bootstrap contract change affecting every future module).

## Implementation checklist (update statuses as you go!)

- [x] Recon of all template files
- [x] 1. Scaffold: csproj, AssemblyReference, SocialModule, slnx entry, Program.cs registration, Api csproj ref
- [x] 2. Common: PermissionScopes.Group + IAclGrantService (+ Identity AclGrantService impl/registration)
- [x] 3. Domain: all entities + errors (Friendship/SocialBlock/Group/GroupMembership/GroupInvite/Conversation/Participant/Message/Notification/UserPrivacySettings/UserPresence)
- [x] 4. Contracts: 16 integration events + ISocialVisibility + ISocialAuthorization + SocialAttachmentTarget
- [x] 5. Application.Abstractions: repos, IUnitOfWork, IOutboxWriter, ISocialQueries (+DTOs), IPresenceStore, IUserReader
- [x] 6. Application handlers: friends (6), blocks (3), groups (15 incl. invites+queries), messaging (8), notifications (3), privacy (2+wire), presence (2) + SocialInteractionGate + Notifier + SocialCursors + GroupGuards + GroupMembershipWriter + ConversationWire
- [x] 7. Consumers: UserBlockedEventHandler (interface method is HandleAsync!)
- [x] 8. Infrastructure: SocialDbContext, 11 configurations, repositories, SocialQueries (ADO), PostgresPresenceStore, UserReader, OutboxWriter, SocialVisibilityReader + SocialAttachmentAuthorizer (in Application/)
- [x] 9. SocialModule wiring complete (DI + messaging consume UserBlocked + seeder)
- [x] 10. Migrations: 20260717034208_InitialSocial (+hand-added ux_friendships_pair LEAST/GREATEST Sql) + 20260717034245_AddSocialViews (SocialViews.Up/Down) + Migrations-folder .editorconfig (generated_code)
- [x] 11. Presentation: all 26 endpoint files (friends 6, blocks 3, groups 13, messaging 7, notifications 3, privacy 2, presence 2)
- [x] 14. Seeding: SeedStreams/SeedTime/SeedPlan (init-properties; Benchmark stays 0 — blocked on Hubert) + SocialSeeder (Order 4; bob admin via real IAclGrantService)
- [x] 15. Build green: Forum.Api + Forum.Modules.Social compile
- [x] 12. Bootstrap realtime: routes-as-data + ViewKind.Group/Conversation + 14 social EventMap arms + Dispatcher per-kind gates (Category | Conversation | TargetUsers with a NobodySees sentinel — NO mutable dispatcher state, it's a singleton)
- [x] 13. Files edits: Dm→Message + GroupIcon, ToSocialTarget, Attach/Detach via ISocialAuthorization, READ GATE in GetFileDownload/ListTargetFiles (AuthorizeFileReadAsync + new IFilesQueries.GetAttachmentRefsAsync), MessageDeleted/GroupDeleted consumers, csproj ref, module wiring
- [x] 16. Tests: Forum.Modules.Social.Tests (25 green) + Api.Tests rewritten for routes/visibility (51 green) + Files.Tests updated (42 green) + ArchitectureTests Social rules (2 green)
- [x] 18. `dotnet format --verify-no-changes` clean + ALL suites green = **238 total** (incl. 38 integration: new migrations+views applied on live Postgres, SeedFlowTests ran SocialSeeder with the real ACL grant, realtime suite passed on the refactored hub)
- [x] 19. Docs: CLAUDE.md Phase 11 entry + Next, DOMAIN-MODEL §6 replaced, permissions-acl group-scope section, REQUIREMENTS Social/A-only/OUT rewrite, ADR 0011 written
- [ ] 17. **THE ONE REMAINING ITEM — `SocialFlowTests` E2E in Forum.IntegrationTests** (ForumApiFactory + TestWait.UntilAsync pattern; InternalsVisibleTo already granted). Scenarios per the brief: friend request → accept → first DM creates conversation → real WS push (subscribe `user`/`conversation` views); group create → invite → accept → group message → push to members via `group` view; block prevents request+DM (same generic 403); privacy NoOne/Friends 403s; group-icon attach replace + message-image attach (additive) + read-gate 403 for non-participant + detach on message delete via relay; presence heartbeat online→offline over threshold. Also verify OpenAPI lists the 26 endpoints (live-host check).

## Session log

- 2026-07-16 (session 1): recon complete; this design locked; starting scaffold.
- 2026-07-17 (session 1 cont.): EVERYTHING built and green except item 17 (SocialFlowTests E2E). 238 tests pass,
  format clean, docs done (CLAUDE.md entry, DOMAIN-MODEL §6, permissions-acl, REQUIREMENTS, ADR 0011).
  Implementation notes: (a) IIntegrationEventHandler's method is HandleAsync; (b) regenerate migrations with
  `dotnet ef migrations add X -p src/Modules/Social/Forum.Modules.Social -s src/Bootstrap/Forum.Api -c
  SocialDbContext -o Infrastructure/Persistence/Migrations` (stray root `Migrations/` folder = forgotten -o);
  (c) seeded bell rows = pending-request + invite only; (d) dispatcher gate uses a static NobodySees sentinel —
  do not reintroduce instance state (singleton!); (e) RealtimeEventMap group-message routes = conversation+group
  views, direct = conversation+both user views. NOTHING COMMITTED — the user commits manually.
