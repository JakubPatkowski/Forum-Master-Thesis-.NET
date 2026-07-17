# Social module — Fable 5 implementation prompts

_Written: 2026-07-16. Companion to `docs/architecture/POST-9C-ROADMAP.md` (Decision 2). This document is the actual, ready-to-paste brief for the two Fable 5 "MaxCode" sessions the Social module is split across (backend, then frontend) — grounded in a direct research pass over the current codebase, not generic advice. Both prompts are self-contained since the two sessions won't share context with each other or with this conversation._

## Before you paste these

A scope flag worth being aware of: `docs/architecture/REQUIREMENTS-AND-ASSUMPTIONS.md` §1 explicitly lists **presence/online status and read-receipts as OUT of scope for the Architecture A/B comparison** ("B may keep these; A does not implement them; neither is measured"). Presence is in this brief anyway, at the user's explicit request, on the same footing as the Redis decision: a deliberate, portfolio/completeness-driven scope expansion, **not** a B-parity requirement. Both prompts say this explicitly so Fable 5 doesn't wire presence into any seed/k6 parity numbers.

---

## Part A — Backend session prompt

```
CONTEXT

You're extending a .NET 10 modular-monolith forum backend (repo: forum-dotnet). It's a master's-thesis
project (Architecture A of an A/B comparison against a colleague's Go SSR monolith, "gomx" — not present
in this repo). The codebase already has four live modules: Identity (+Authz/ACL), Content, Files,
Engagement — plus a Bootstrap host (Forum.Api) that owns a WebSocket realtime hub and the HTTP pipeline.
Read CLAUDE.md at the repo root first — it's the authoritative, continuously-updated project memory and
describes every existing module's shape, the golden rules, and the current phase status. This session's
job is Phase 5: a new Forum.Modules.Social module.

This is a SEPARATE, CONTEXT-ISOLATED SESSION from a companion frontend session that will build the SPA
side against whatever API surface you ship. It won't see your reasoning — only your code, DTOs, and
whatever you write into CLAUDE.md / the domain-model doc. Make your endpoint contracts self-descriptive
(OpenAPI already reflects minimal-API endpoints automatically in this repo — keep using that convention)
so the frontend session isn't blocked on asking you anything.

SCOPE

Friends (send/accept/decline a friend request, remove a friend, block a user), direct messages, groups
(create, invite, join, leave, membership roles) with a group chat, notifications, and presence (online/
away/offline). Everything that needs to be live must push over the EXISTING WebSocket hub — not a new
polling endpoint. Redis is explicitly a SEPARATE, already-scoped future session (see
docs/architecture/POST-9C-ROADMAP.md Phase 1) — do not add a Redis dependency here. Your one job
regarding Redis is to leave a clean seam (described below, for presence) that session can plug into
without touching your call sites.

Groups exist because the colleague's app (gomx) already has them — friends+DMs alone is no longer enough
for a fair comparison. Presence, by contrast, is NOT part of the A/B comparison (REQUIREMENTS-AND-
ASSUMPTIONS.md §1 lists "presence/online" as explicitly out of scope for that comparison) — build it
anyway, but don't let it leak into any B-parity seed counts or k6 traffic-mix percentages.

WHAT ALREADY EXISTS THAT YOU MUST REUSE, NOT REINVENT

1. ACL / permissions — `forum_authz.acl_entries` has a bare `scope text` column with NO CHECK constraint
   restricting its values (confirmed by reading the raw-SQL migration in
   `Forum.Modules.Identity/Infrastructure/Acl/AuthzSchema.cs`); `effective_mask()`/`has_permission()`/
   `recompute_user_perms()` all take `scope`/`scope_id` generically. `docs/db/permissions-acl-design.md`
   already anticipates a `group` scope ("group optional later"). Concretely: add
   `PermissionScopes.Group = "group"` to `Forum.Common/Security/PermissionScopes.cs` — ZERO schema
   changes needed. Call it exactly the way Content calls category scope today
   (`Content/Application/Threads/CreateThread.cs`, `ContentAuthorization.IsOwnerOrModeratorAsync`):
   `ICurrentUser.HasPermissionAsync(action, PermissionScopes.Group, groupId)` inside your own handlers,
   or the explicit-`userId` `IPermissionService.HasPermissionAsync(userId, action, scope, scopeId)`
   overload when checking someone who ISN'T the current request's actor (e.g. the realtime dispatcher
   checking a subscriber — see Files' `ContentAttachmentAuthorizer` for the exact precedent).
   BEFORE adding new permission actions (e.g. `group.manage`/`group.invite`), inspect the existing
   `forum_authz.actions` seed data and the bitmask's bit budget (`docs/db/permissions-acl-design.md`,
   `int_or_agg`) — confirm there's room before allocating new bits, and say in your writeup how many
   bits were free vs. used.
   Design split: group MEMBERSHIP (is this user even in the group at all) is a fact your own module owns
   in a join table — ACL only decides what a member may DO once they're in (invite others, remove
   members, rename the group = admin-level; post messages = member-level). This mirrors Category
   ownership/visibility (Content-owned) vs. moderate permission (ACL-owned) exactly.

2. `UserBlockedIntegrationEvent` (Identity's Contracts) is an ADMIN MODERATION BAN
   (`User.Block(by)` flips `UserStatus` to `Blocked`, `BlockedBy` = the acting admin) — it is NOT a
   peer-to-peer social block, and reusing it for "block a friend" would be a semantic bug. Build your
   own, clearly differently-named concept (suggest `SocialBlock`, not `UserBlock` — avoid anything that
   reads like Identity's `UserStatus.Blocked`/`UserBlockedIntegrationEvent` in code or docs; this naming
   collision risk is worth a sentence in your own writeup). DO additionally consume the existing
   `UserBlockedIntegrationEvent` as a new third consumer (Content's own handler is still log-only per
   CLAUDE.md's Phase 6 notes) — e.g. auto-decline pending friend requests/invites involving a
   newly-suspended account. That's additive, not a substitute.

3. Messaging/outbox — copy Engagement's exact template
   (`Forum.Modules.Engagement/Infrastructure/Messaging/OutboxWriter.cs` +
   `EngagementModule.cs` lines ~29-45): a module-local `IOutboxWriter`/`OutboxWriter` pair on your own
   `SocialDbContext`, and
   `services.AddModuleMessaging<SocialDbContext>("social", m => m.Consume<UserBlockedIntegrationEvent>())`
   in `SocialModule.cs`. `MessagingTopology.SourceExchange` derives the exchange from the event's CLR
   namespace — putting your events under `Forum.Modules.Social.Contracts.IntegrationEvents.*` gets you
   exchange `"social"` for free, no registration needed beyond the above.

4. The realtime hub is 100% Bootstrap-owned, with NO plugin mechanism — CLAUDE.md's own Phase 7 summary
   says this ("the hub is host wiring, not a module"). Concretely, `Forum.Api/Realtime/RealtimeEventMap.cs`
   hardcodes a `switch` over Content/Engagement CLR event types, `SubscriptionSet.cs`'s `ViewKind` enum is
   a closed `Category/Thread/User` set with a hardcoded string-parse switch, and
   `RealtimeNotificationDispatcher.cs` hardcodes `IContentVisibility` as the one and only per-push
   authorization source. **You will need to edit Bootstrap code directly** — this is expected, not a
   module-boundary violation:
   - `RealtimeEventMap.cs` — add your integration event types to `ConsumedEvents` + new `switch` arms in
     `TryMap`.
   - `SubscriptionSet.cs` — add `ViewKind.Group`/`ViewKind.Conversation` (or whatever names you settle
     on) + parse cases + match branches.
   - `RealtimeNotificationDispatcher.cs` — currently resolves exactly one visibility port
     (`IContentVisibility`) per push. You need a second one for group/conversation-scoped events (see
     Contracts surface below) — decide whether to branch on notification kind to pick the right port, or
     generalize the dispatcher's visibility step into a small per-kind provider lookup. Either is fine;
     document which you chose and why.
   - `RealtimeNotification.cs`'s shape is `(CategoryId, ThreadId, ActorUserId)` — Content/Engagement-
     specific. Bolting Social's `(ConversationId/GroupId)` onto those same field names would be a wart.
     Consider generalizing to something like `(ScopeType, ScopeId, ActorUserId)` now that a second module
     needs this shape — it's a legitimate refactor, not scope creep, since a future module would hit the
     exact same wall. Your call; justify whichever way you go.
   - `Program.cs` — register `new SocialModule()` in the explicit `IModule` list alongside the other four.
   - Subscribe-time authorization precedent: category/thread views are ALWAYS accepted at subscribe time
     ("subscribe is always accepted — authorization happens on every push," so a later revocation still
     gates correctly); the `User` view is subscribe-time-gated to self only. Decide which precedent fits
     `group` (membership can change mid-connection, so the category precedent — accept-then-authorize-
     per-push — is probably the safer default) vs. `conversation` (a DM's two participants never change
     after creation, so a subscribe-time check might be defensible there) and say which you picked.

   Contracts surface Social must expose for the dispatcher to call (mirror `IContentVisibility` /
   `CategoryAccessReader` 1:1):
   - `ISocialVisibility.GetGroupAccessAsync(groupId) -> GroupAccess?` — membership-based (unlike public
     categories, there's no "anyone can see this" case for a private group's chat — decide if groups can
     ever be public-readable, or if membership is always required).
   - `ISocialVisibility.GetConversationAccessAsync(conversationId, userId) -> bool` — participant check.

5. Files module attachment support for group icons / message images needs YOU to edit Files directly —
   this is NOT DI-pluggable from outside (confirmed: `IContentAuthorization` is a closed, Content-specific
   contract keyed to a `ContentAttachmentTarget` enum; there's no generic target-type registry Files scans).
   The exact stub you're closing: `Files/Application/Attachments/AttachFile.cs` (~line 70-78) already has
   `if (targetType == FileTargetType.Dm) return Result.Failure(FileErrors.DmAttachmentsNotSupported);` with
   the error message literally reading *"Direct-message attachments arrive with the Social module (Phase 5)."*
   Required edits (mirror Content's `IContentAuthorization`/`ContentAttachmentAuthorizer` shape exactly):
   - `Files/Domain/Files/FileTargetType.cs` — extend the enum (repurpose `Dm`, add `GroupIcon` and
     whatever you need for group/DM message images).
   - `Files/Application/FileTargets.cs` — wire-string mapping (`TryParse`/`ToWire`) + a new
     `ToSocialTarget(...)` mapper (parallel to the existing `ToContentTarget`, since
     `ContentAttachmentTarget` can't represent Social's targets).
   - `Files/Application/Attachments/AttachFile.cs` and `DetachFile.cs` — remove the `Dm` 422 stub, inject
     a new `ISocialAuthorization` port (mirror `IContentAuthorization`'s shape:
     `AuthorizeAttachmentAsync(target, targetId, userId, ct) -> Result`), and add your "replace" target
     (group icon) to the hardcoded replace-arm list at `AttachFile.cs` ~lines 112-126 (currently
     `Avatar`/`CategoryIcon`/`ThreadIcon`) — everything else (message images) falls into the additive
     branch capped by `FilesOptions.MaxAttachmentsPerTarget`, which today is ONE global constant with no
     per-target override; decide if message images need their own cap and extend `FilesOptions` if so.
   - `Forum.Modules.Files.csproj` — add a `<ProjectReference>` to `Forum.Modules.Social` (Contracts),
     mirroring the existing one to Content.
   - `Forum.ArchitectureTests/ModuleBoundaryTests.cs` — extend Files' permitted-cross-module-Contracts
     rule to allow `Forum.Modules.Social.Contracts`, same shape as the existing Content/Identity rules.
   - Real gap to address, don't silently inherit: `GetFileEndpoint`/`ListTargetFilesEndpoint` are
     ANONYMOUS today (no auth check at all — reads rely purely on ULID-unguessability + a short presign
     TTL, which is a deliberate, documented tradeoff for PUBLIC thread/comment images). A private DM/group-
     message image has no such excuse. Decide and justify: add a read-side authorization check (mirroring
     the write-side `ISocialAuthorization` port) for message-image reads, or explicitly accept and document
     the same ULID-unguessability tradeoff for this case too. Don't leave it unconsidered.
   `IObjectStorage`/MinIO itself needs zero changes — it's fully target-type-agnostic already.

6. Seeding — implement `IModuleSeeder` (`Forum.Modules.Social/Infrastructure/Seeding/SocialSeeder.cs`),
   register via `services.AddScoped<IModuleSeeder, SocialSeeder>()` in `SocialModule.cs`
   (`EngagementSeeder` is your copy-paste template), extend the shared `SeedPlan`/`SeedStreams`/
   `SeedTime` in `Forum.Infrastructure/Seeding/` with your counts/streams. Development-profile counts are
   yours to pick (keep them small, matching the existing Development profile's spirit — a handful of
   friendships/groups/messages, enough for a demo). Benchmark-profile counts are explicitly BLOCKED —
   Hubert (the gomx author) hasn't confirmed his numbers yet (see `docs/architecture/
   POST-9C-ROADMAP.md` Decision 3) — leave Benchmark counts at 0 / a clearly-marked TODO, don't guess.

7. There IS an existing schema sketch in `docs/architecture/DOMAIN-MODEL-AND-DATABASE.md` §6
   (`friendships` + `direct_messages`, two tables) — it is STALE (dated 2026-06-24, predates Files/file-
   attachments entirely, has no groups, explicitly says "optional"/"voice is OUT of scope" framing that's
   now superseded). Treat it as a discarded first draft, not a locked contract — replace that whole
   section with your real shipped schema when you're done, per the docs requirement below.

PROPOSED DOMAIN MODEL — a starting point, not a mandate

This is my own synthesis from reading the codebase, offered as a reasonable default. If you find a
genuinely better shape, THINK IT THROUGH, BUILD YOUR VERSION, AND JUSTIFY THE DEVIATION IN WRITING —
the same way this codebase already documents its own deviations (e.g. CLAUDE.md's Phase 3 "KEY
DEVIATION" callout about Files/Content's attachment-authorization direction). Don't just transcribe this
verbatim if you see a real problem with it.

- `Friendship` (RequesterId, AddresseeId, Status: Pending/Accepted, audit) — send/accept/decline/remove.
- `SocialBlock` (BlockerId, BlockedId, CreatedOnUtc) — unilateral, asymmetric; checked before allowing a
  friend request, DM, or group invite FROM the blocked party TO the blocker.
- `Group` (Id, Name, Description, OwnerId (IOwned), Visibility, IconFileId, audit, soft-delete).
- `GroupMembership` (GroupId, UserId, JoinedAt, InvitedBy) — the source of truth for "is this user in the
  group"; role-level permission (owner/admin/member) is expressed via ACL at `scope="group"` (see above),
  NOT duplicated as a column here.
- `GroupInvite` (GroupId, InvitedUserId, InvitedByUserId, Status, CreatedOnUtc).
- `Conversation` (Id, Type: Direct/Group) + `ConversationParticipant` (ConversationId, UserId, JoinedAt,
  LeftAt, LastReadAt, IsMuted) + `Message` (Id, ConversationId, SenderId, Body markdown, CreatedOnUtc,
  EditedOnUtc, IsDeleted-tombstone like `Comment.Delete`'s `"[deleted]"` convention) — a SINGLE unified
  messaging pipeline for both DMs and group chats, rather than two parallel Message tables. For a group's
  chat, consider literally reusing the Group's own id as its Conversation's id (Type=Group) instead of a
  separate nullable FK — avoids an indirection. For a DM, get-or-create the Direct conversation lazily on
  first message (don't pre-create one for every friendship — most friends never DM). Keep
  `ConversationParticipant` as the ONE membership check used by every message read/send handler
  regardless of Direct vs. Group (group membership changes write through to it in the same transaction),
  so there's exactly one authorization code path instead of branching by conversation type.
  Note: `LastReadAt` is for the OWNER's own unread-count badge (TopNav already has hardcoded placeholder
  badges "2"/"3" waiting to be wired to something real) — this is NOT read receipts (which would expose
  to the SENDER that the recipient has read their message, and is explicitly out of scope per
  REQUIREMENTS-AND-ASSUMPTIONS.md §1). Don't build the receipt-to-sender direction.
- `Notification` (Id, UserId, Kind, RelatedEntityType/Id, IsRead, CreatedOnUtc) — the DURABLE source of
  truth for the notification bell / unread count. Per ADR 0010 (read it in full —
  `docs/architecture/adr/0010-websocket-realtime-aggregate-changes.md`) and its shipped deviation
  documented in `Forum.Api/Realtime/ChangeNotification.cs`: the WS push for a notification must be
  "identity + routing, go re-fetch" — NEVER carry the notification's actual content in the socket
  payload. Mirror `ChangeNotification`'s shape/rationale exactly.
- `UserPrivacySettings` (UserId PK, WhoCanSendFriendRequest, WhoCanMessage, WhoCanInviteToGroup,
  ShowOnlineStatus) — keep this deliberately simple (no domain events, no audit ceremony beyond what's
  needed) since it's pure preference state, not audited business data. Enforce these in the relevant
  handlers (send-friend-request, send-message, invite-to-group) following the existing 404→403→422
  ordering convention.
- Presence — NOT a normal audited aggregate; it's ephemeral, high-write-frequency state. Define an
  `IPresenceStore` port now:
  `Task Heartbeat(userId)`, `Task<PresenceStatus> GetStatus(userId)`,
  `Task<IReadOnlyDictionary<Ulid,PresenceStatus>> GetStatuses(IEnumerable<Ulid> userIds)` (batch, mirror
  Engagement's reaction-batch pattern — one round trip, not N). Ship ONE non-Redis implementation now
  (suggest: a plain `user_presence` table, UserId PK + LastHeartbeatAt, "online" computed as
  `now() - LastHeartbeatAt < threshold` at read time, no background sweep needed). This interface is the
  exact seam the ALREADY-SCOPED future Redis session will plug a Redis-backed implementation into with
  zero caller changes (this repo already has a documented precedent for exactly this shape — the WS
  ticket replay cache's "if reversed" recipe in `PHASE-9-10-ENTERPRISE-PLAN.md` §10d describes a
  config-gated `Memory|Redis` strategy swap; apply the same idiom here proactively). Decide and document
  your heartbeat trigger (a lightweight WS protocol ping every ~20-30s, vs. deriving it from connection
  lifecycle) — either is defensible, just justify it.

WHAT TO FOLLOW FROM HOUSE STYLE (non-negotiable, this is what "enterprise-grade, consistent with the
rest of the codebase" means here)

- Module shape: `Forum.Modules.Social` with `Domain/ Application/ Infrastructure/ Presentation/
  Contracts/` folders, `SocialModule : IModule`, schema `forum_social`, its own `SocialDbContext` +
  EF migrations (ordinary EF migrations for your tables — raw-SQL migrations in this codebase are
  reserved for cross-cutting things like FTS triggers or the ACL schema itself, not ordinary tables).
- CQRS without MediatR, Result/Result<T> (no exceptions for expected failures), 404→403→422 ordering in
  every handler, Scrutor-scanned handler registration.
- Reads via SQL views + a raw-ADO query layer (mirror `Content/Infrastructure/ContentQueries.cs`) —
  friend list, group member list, conversation list (with last-message preview + unread count), and
  message history ALL need real views, not EF LINQ over the write model. Message history and any
  potentially-large list MUST use keyset pagination (mirror `ix_threads_feed` / `ThreadFeedCursor`) — no
  OFFSET anywhere.
- Minimal API `IEndpoint` — one file per endpoint under `Presentation/`.
- Audit interceptor + ULID everywhere, snake_case DB naming, soft-delete where it applies (Group, at
  least — Friendship/Message probably don't need it; your call, justify it).
- Do NOT add a Redis package or dependency in this session.
- Do NOT create git commits or pushes — stage changes only; per this repo's CLAUDE.md, commits/pushes are
  the human's job, always.

TESTING (required, not optional)

- Unit: `Friendship`/`SocialBlock` state transitions, group-ownership invariants (what happens if the
  sole owner leaves? decide and test it), `Message` edit/delete-tombstone, privacy-setting gates,
  presence staleness computation.
- Application/handler tests mirroring the existing `ContentFlowTests`/`EngagementFlowTests` naming and
  structure — Result pattern + 404→403→422 ordering for every use case.
- `ModuleBoundaryTests` — Social's own boundary rule, plus the new Files→Social.Contracts permitted
  direction.
- Integration E2E (mirror the existing `ForumApiFactory` + `TestWait.UntilAsync` polling pattern — Phase
  6 retired direct-handler-invocation in favor of polling the real relay→exchange→consumer pipeline, do
  the same here): friend request → accept → first DM creates a conversation → real WS push; group create
  → invite → accept → group message → realtime push to every member; a block prevents a friend
  request/DM; a privacy setting blocks a DM with the right 403/422; group-icon attach (replace) and
  message-image attach (additive, capped) including detach on message delete; presence heartbeat
  reflects online→offline over the threshold.

DOCUMENTATION (required — update these when you're done, don't leave it to the frontend session or the
user to reverse-engineer from your diff)

- `CLAUDE.md` "Current state" — a real Phase 5 entry replacing the current "CONFIRMED, not yet built"
  placeholder, in the same style/rigor as every other phase entry (schema DDL summary, use cases, events,
  guardrails, verified-green test counts). Also update the "Next:" closing paragraph.
- `docs/architecture/DOMAIN-MODEL-AND-DATABASE.md` §6 — replace the stale sketch with your real schema.
- `docs/db/permissions-acl-design.md` — document the now-real `group` scope usage and the bits you
  allocated.
- `docs/architecture/REQUIREMENTS-AND-ASSUMPTIONS.md` — update Social's scope description (groups are
  now mandatory for B-parity, not optional friends+DM-only; presence is an A-only addition, not measured).
- A new ADR (next number: 0011) IF you introduce something genuinely architecture-level (e.g. the
  Bootstrap realtime hub's generalization, or the presence/Redis-readiness pattern) — your call whether
  it rises to ADR-worthy or is just a CLAUDE.md paragraph; this codebase already has a precedent (ADR
  0010) for exactly this kind of realtime-design decision.

Work through this in whatever order makes sense to you (a reasonable one: domain → EF migration →
application handlers → endpoints → Contracts/events → messaging wiring → Bootstrap/realtime wiring →
Files integration → ACL group scope → presence → seeding → tests → docs) — reorder if you have a better
sequencing rationale, just don't skip a step.
```

---

## Part B — Frontend session prompt

```
CONTEXT

You're wiring a real Social feature into a Next.js 15 (App Router, pure client-side-rendered, no Next
server ever talks to the API) React SPA — the frontend for a .NET forum backend (repo: forum-dotnet).
Read `frontend/README.md` first — it lists known gaps including the exact one you're closing. This is a
SEPARATE, CONTEXT-ISOLATED SESSION from a companion backend session that built (or is building) a new
`Forum.Modules.Social` API surface — you won't see its reasoning, only its shipped endpoints/DTOs
(check the live OpenAPI doc / ask the user for the backend session's summary if the API isn't self-
evident). Presence is in scope at the user's explicit request even though
`docs/architecture/REQUIREMENTS-AND-ASSUMPTIONS.md` §1 lists presence as out of the A/B comparison scope
— it's a real feature here, just not something the comparison measures.

WHAT EXISTS TODAY (confirmed by direct code reading — build on this, don't rebuild it)

- `frontend/src/app/social/page.tsx` (+ `social.module.css`) is a PURE MOCK — zero fetches, zero
  `useQuery`/`useMutation`, zero `useRealtimeSubscription`, everything is local `useState` over hardcoded
  arrays (`FRIENDS`, `INITIAL_REQUESTS`, `GROUPS`, `INITIAL_CHAT`). Its own header comment says so
  explicitly. Reusable AS MARKUP/CSS: the 4-tab left rail (Friends/Requests/Groups/Ignored), the
  avatar-with-status-dot pattern, the chat panel's message-bubble styling (already has "me" vs "them"
  alignment). NOT reusable: every interactive affordance (accept/decline, send, open chat) is a
  local-state-only stub with no group-detail view, no create/invite UI, no notification list, no real
  presence. Delete the persistent PREVIEW banner (lines ~104-113) once wired for real.
- `frontend/src/components/layout/TopNav.tsx` has TWO hardcoded unread-count badges ("2" for friends,
  "3" for messages, ~lines 266-277) pointing at `/social` — wire these to real counts.
- Realtime — `frontend/src/lib/api/types.ts`: `RealtimeViewKind = "category" | "thread" | "user"` and
  `ChangeNotification.entity: "thread" | "comment" | "reaction"` are the exact extension points. Add
  `"group" | "conversation"` to the view-kind union and whatever new entity values the backend session's
  event types need (e.g. `"friendRequest" | "groupInvite" | "message" | "presence"` — match whatever the
  backend actually calls them). `RealtimeSocketManager` itself
  (`frontend/src/lib/realtime/socket-manager.ts`) is fully generic over `(view, id)` — no changes needed
  there. Subscribing from a page is one line:
  `useRealtimeSubscription("group", groupId)` / `useRealtimeSubscription("conversation", conversationId)`
  (`frontend/src/lib/realtime/realtime-context.tsx`'s `useRealtimeSubscription` hook — copy the exact
  pattern used by `frontend/src/app/t/[id]/page.tsx` line ~62).
- `frontend/src/lib/realtime/invalidation.ts` — a `switch (notification.entity)` mapping each kind to a
  targeted `queryClient.invalidateQueries(...)`, following the existing `"reaction"` case's predicate-
  based batch-invalidation shape (lines ~37-58) as your template. Add cases for every new entity.
- **`PUSH_COVERED_KEY_ROOTS`** in `realtime-context.tsx` (~line 58) — a hardcoded
  `Set(["threads","comments","reactions"])` controlling what gets refetched on reconnect. YOU MUST add
  your new social query-key roots here or reconnects will silently fail to resync social data that
  changed while disconnected.
- Notification bell / activity log — `frontend/src/components/panels/LiveActivityPanel.tsx` and
  `TopNav.tsx`'s `ActivityBell()` are both generic over `entity`/`type` (fed straight from
  `useRealtime().activity`, capped at 20 entries) — new entity values flow into the SAME rolling log
  automatically, but their RENDERING (icon/label/color, currently only special-cased for `"reaction"`)
  and `frontend/src/lib/realtime/notification-href.ts` (currently returns `undefined` — no link — for
  anything besides thread/comment/reaction) both need new cases or your new notification kinds will
  render as generic unclickable rows.
- Presence has NO existing pattern anywhere in the realtime layer — it's genuinely new. Decide whether
  it's its own `ChangeNotification.entity: "presence"` case or a separate WS message type living outside
  the `ChangeNotification` union (if the latter, `isChangeNotification()` in `types.ts` ~line 295 must
  keep correctly excluding it).
- API layer convention — copy `frontend/src/lib/api/engagement.ts` (flat object of one-line `apiFetch<T>`
  calls) + `frontend/src/lib/api/keys.ts` (query-key factory, `[root, ...args] as const` shape) +
  `frontend/src/lib/hooks/use-reactions.ts` (React Query hooks with optimistic update + rollback, e.g.
  `useToggleReaction`'s `onMutate`/`onError`/`onSuccess` shape) EXACTLY, for a new `socialApi` /
  `queryKeys.friends()` etc. / `use-social.ts` trio. `apiFetch`/`problem.ts` (RFC7807 handling, single-
  flight 401 refresh) are already generic — you get them for free.
- Markdown composer — `frontend/src/components/compose/MarkdownEditor.tsx` is FULLY GENERIC (props:
  `{value, onChange, rows?, placeholder?, onUploadImage?, compact?}`, no thread/comment-specific state) —
  reuse it as-is for the message composer, just pass your own `onUploadImage` implementation (see
  `ComposeThreadModal.tsx` ~line 92-95 for the exact glue-code shape:
  `uploads.add(file)` → returns a committed `fileId` → passed straight into the editor). The upload
  pipeline (`frontend/src/lib/upload/upload.ts`, `use-upload-manager.ts`, `frontend/src/lib/api/files.ts`)
  is also fully reusable. IMPORTANT: `FileTargetType` in `types.ts` (~line 182-188) ALREADY includes
  `"dm"` in its union, and the comment on `filesApi.attach` already says `dm: 422 (unbuilt)` — this
  literally becomes real once the backend session ships DM/group-message attachment support. Don't
  build your own upload plumbing; just point the existing one at the new target type(s) the backend
  session defines (confirm the exact wire strings with its Files-module changes — it may add
  `group_icon` alongside or instead of `dm`).
- Auth — `useAuth().currentUser?.id`/`.username` (from `frontend/src/lib/auth/auth-context.tsx`) is
  synchronously available anywhere in the tree the moment a token exists — no extra round trip needed
  for "who am I" in presence/notification UI.
- `frontend/README.md` line ~131-132 has the exact gap-note wording to remove once this is wired for
  real: *"Social page (`/social`) — UI-only preview with local state; the backend has no Social module.
  A persistent PREVIEW banner says so."*

WHAT TO BUILD

- Replace the mock `/social` page's data layer entirely: real fetches/mutations for friend list +
  requests (send/accept/decline/remove), group list + detail (create/invite/join/leave/member list with
  role), a real group-chat and DM composer/history view (reuse `MarkdownEditor` + the render side of the
  existing `image:`/`@video()` media convention — `frontend/src/lib/markdown/media-convention.ts` — for
  message bodies), real presence dots (online/away/offline) driven by the new realtime entity, and a real
  notification list/unread-count wired to the two `TopNav` badges.
- A privacy-settings section/page for the new preference toggles the backend session ships (who can
  friend-request/DM/invite-to-group me, show-online-status).
- Group icon upload reusing the existing presigned-upload attach flow (same shape as avatar/category
  icon replace-semantics elsewhere in the app).
- Keep the existing tab/rail/chat-panel visual language from the mock (it's reasonable CSS) but strip
  every local-state stub in favor of the real API/realtime wiring above.

TESTING (required)

- Vitest unit tests mirroring the existing precedents (`feed-merge`, `socket-manager`,
  `invalidation-mapping` test files) for any new client-side logic you introduce — e.g. conversation-list
  sort/merge, notification dedup, presence-status derivation from heartbeat timestamps.
- Manually exercise the golden path in a real browser against the live backend before calling this done
  (per this repo's CLAUDE.md rule: UI changes must be verified by actually using the feature, not just
  type-checked) — friend request → accept → DM round trip with a live WS push visible in another
  session/tab, group create → invite → join → group message live-pushed to all members, presence
  indicator flips on connect/disconnect.

DOCUMENTATION (required)

- Update `frontend/README.md` — remove the "Social UI-only mock" gap note, add any new gaps you find
  (e.g. anything the backend session left as a TODO that blocks a nice-to-have UI affordance).

Do NOT create git commits or pushes — stage changes only; commits/pushes are the human's job, always.
```
