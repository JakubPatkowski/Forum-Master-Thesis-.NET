# Social module — Fable 5 implementation prompts

_Written: 2026-07-16. Companion to `docs/architecture/POST-9C-ROADMAP.md` (Decision 2). This document is the actual, ready-to-paste brief for the two Fable 5 "MaxCode" sessions the Social module is split across (backend, then frontend) — grounded in a direct research pass over the current codebase, not generic advice. Both prompts are self-contained since the two sessions won't share context with each other or with this conversation._

## Before you paste these

A scope flag worth being aware of: `docs/architecture/REQUIREMENTS-AND-ASSUMPTIONS.md` §1 explicitly lists **presence/online status and read-receipts as OUT of scope for the Architecture A/B comparison** ("B may keep these; A does not implement them; neither is measured"). Presence is in this brief anyway, at the user's explicit request, on the same footing as the Redis decision: a deliberate, portfolio/completeness-driven scope expansion, **not** a B-parity requirement. Both prompts say this explicitly so Fable 5 doesn't wire presence into any seed/k6 parity numbers.

---

## Part A — Backend session prompt

**STATUS: DONE, committed 2026-07-17 (commit `3755e57`, branch `27-feat-phase-11---social`).** Everything
below was built essentially as proposed, with a few improvements the backend session made and justified —
see `CLAUDE.md`'s "Phase 11 (plan Phase 5)" entry for the authoritative as-built summary, and
`docs/architecture/PHASE-11-SOCIAL-PROGRESS.md` for the full locked-design log (238 tests green; only the
dedicated `SocialFlowTests` E2E suite is still outstanding). Kept here for historical/design context — Part
B below has been corrected against the REAL shipped API surface, not this proposal, so treat Part B as
ground truth and this Part A prompt as "what was asked for," not "what necessarily still matches reality."

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

**Corrected 2026-07-17 against the REAL shipped backend** (commit `3755e57`) — the original draft guessed
at several things the actual implementation resolved differently (presence is poll-only, not WS-pushed;
the file target type is `"message"` not `"dm"`; entity names include `group_member`/`group_invite`
distinctly). Every fact below was verified by reading the actual backend source and the actual frontend
component library — not re-guessed.

```
CONTEXT

You're wiring a real Social feature into a Next.js 15 (App Router, pure client-side-rendered, no Next
server ever talks to the API) React SPA — the frontend for a .NET forum backend (repo: forum-dotnet).
Read `frontend/README.md` first. The backend is DONE and committed (`Forum.Modules.Social`, 36 endpoints
under `/api/social/*`, full realtime wiring) — this is not speculative integration work, the API below is
exact, verified against the shipped source on 2026-07-17. This is a SEPARATE, CONTEXT-ISOLATED SESSION
from the backend one; you won't see its reasoning, only what's documented here and in `CLAUDE.md`'s
"Phase 11 (plan Phase 5)" entry / `docs/architecture/PHASE-11-SOCIAL-PROGRESS.md`.

Presence is in scope at the user's explicit request even though
`docs/architecture/REQUIREMENTS-AND-ASSUMPTIONS.md` §1 lists presence as out of the A/B comparison scope
— it's a real feature here, just not something the comparison measures, and not something to fold into
any B-parity number.

═══════════════════════════════════════════════════════════════════════════
COMPONENT ARCHITECTURE & PERFORMANCE — READ THIS SECTION FIRST
═══════════════════════════════════════════════════════════════════════════

The user's explicit bar for this work: written in the SAME STYLE as the rest of the app, built from
ATOMIC, REUSABLE, EASY-TO-EDIT components — not a bespoke one-off page — and EFFICIENT about data and
icon/avatar fetching. Concretely:

1. **The current `/social` mock page is NOT the architecture to copy.** It hand-rolls every row as a
   plain `<div>` with page-local CSS classes, never uses the shared `Avatar` component (it fakes an
   avatar with `friend.initial` in a styled `<span>`), and has no reusable row/card components at all.
   Treat it as disposable scaffolding for its CSS only — the actual component architecture must follow
   the pattern used elsewhere in this app for real, non-trivial lists:
   - `frontend/src/components/thread/ThreadCard.tsx` — a proper reusable card component
     (`thread`/`reaction`/`showCategory`/`pinAction`/`isNew` props), used identically across feed/search/
     category/profile pages.
   - `frontend/src/components/comments/CommentNode.tsx` — a reusable recursive row component.
   Build a new `frontend/src/components/social/` folder (mirroring the `components/thread/` and
   `components/comments/` convention) containing real, props-driven, reusable components: e.g.
   `FriendRow`, `FriendRequestCard`, `GroupCard`, `GroupMemberRow`, `ConversationPill` (a minimized chat
   pill), `ChatWindow`, `MessageBubble`, `NotificationRow`. Each should take data + callbacks as props and
   contain NO page-specific logic, exactly like `ThreadCard` does — easy to reuse across the friends list,
   a group's member list, search-style discovery, etc., and easy to edit later because each is a small,
   single-purpose file.

2. **Reuse the existing atomic UI library — do not reinvent any of these** (`frontend/src/components/ui/`):
   `Avatar` (per-user avatar w/ monogram fallback), `Badge` (status chips), `LiveDot` (pulsing status dot —
   already supports the exact online/away/offline color semantics you need for presence), `Button`,
   `Input`, `Panel` (sidebar/section card), `Modal`, `EmptyState`, `ErrorState.tsx`'s `ApiErrorState` /
   `InlineErrorBanner` (route every social mutation's failure through this, not a hand-rolled error div),
   `Skeleton` (+ the `ThreadCardSkeleton` geometry-matching pattern — build an equivalent
   `SocialRowSkeleton` if your new rows need one), `LoadMoreButton` (cursor "LOAD MORE ↓", never page
   numbers), `LiveBanner` (for "new message arrived" style announcements, mirroring how new threads are
   announced rather than silently inserted), `TagChip`, `CornerBrackets`, `Monogram`, and the
   `CategoryIcon`/`ThreadIcon` per-target-file-lookup pattern (build a `GroupIcon` component that's a
   structural copy of `CategoryIcon.tsx`, pointed at the new `group_icon` file target — see below).
   `toast` (`useToast().showError(apiError)`) is the standard place to surface a failed mutation.

3. **Icon convention: hand-authored inline SVG, no icon library exists in this app.** Every icon
   (`TopNav.tsx`'s bell/friends/messages icons, `ReactionButton`'s like icon, `ThreadCard`'s pin icon) is
   a literal `<svg viewBox="0 0 24 24" fill="currentColor">...</svg>` (or `stroke="currentColor"` for
   line-style icons) written inline at the call site, sized via `width`/`height`. Any new icon you need
   (person-add, group, chat-bubble) should be authored the same way, inline, at its own use site — do NOT
   introduce a new icon library/dependency for this.

4. **Efficient data fetching — the concrete rules, derived from what's already in this codebase:**
   - Friends, friend requests, groups, group members, group invites, conversations, messages, and
     notifications are ALL covered by realtime WS push (see the exact entity/route table below) — use
     `staleTimes.realtimeCovered` (5 min, `frontend/src/lib/api/stale-times.ts`) for every one of these
     queries, exactly like `threads`/`comments`/`reactions` do today. Do not invent a shorter poll
     interval for them; the WS layer keeps them fresh while mounted, `staleTime` only governs
     back-navigation.
   - **Presence is the one exception — it is NOT WS-covered by backend design** (confirmed: "presence
     never on the bus"). It needs its OWN mechanism, separate from `staleTime`: (a) a periodic
     `POST /api/social/presence/heartbeat` fired on a ~30s interval while the tab is active/visible
     (use the Page Visibility API to pause it when hidden, saving needless requests) via a small custom
     hook, e.g. `usePresenceHeartbeat()`; (b) a `GET /api/social/presence?userIds=id,id,...` query for
     whichever users are currently visible (friend list, group member list, a DM's other participant)
     using React Query's `refetchInterval` (e.g. 20-30s), NOT `staleTime` — this is active polling, a
     different mechanism from the push-invalidation pattern. Batch every visible userId into ONE call
     per view (mirror `useReactionBatch`'s one-round-trip-many-ids shape) — never fire one presence
     request per row.
   - Avatars/group icons reuse the EXISTING `presignedFiles` staleTime tier (5 min) via the existing
     `Avatar`/new-`GroupIcon` components — no new tier needed there. Be aware (don't "fix" this, it's
     consistent with the rest of the app, just don't make it WORSE): each component fetches
     `GET /api/files?targetType=...&targetId=...` per distinct id — there is no batch-files endpoint
     anywhere in this app (Files module has none, and adding one is backend work out of this session's
     scope). Because every social list is keyset-paginated with a bounded page size (except the
     conversation list, hard-capped at 200), this reproduces exactly the same bounded per-page fetch
     pattern `ThreadCard`/`CategoryIcon` already use for feeds — not a regression, just don't make it
     worse by e.g. rendering an unbounded/unpaginated avatar-heavy list anywhere.
   - Extend **`PUSH_COVERED_KEY_ROOTS`** in `frontend/src/lib/realtime/realtime-context.tsx` (~line 58)
     with every new social query-key root (`friends`, `friendRequests`, `groups`, `groupMembers`,
     `groupInvites`, `conversations`, `messages`, `notifications`) — otherwise reconnects won't resync
     social data that changed while disconnected.

5. **Visual/interaction target: follow `frontend/design-reference/Social.dc.html`, not the throwaway
   mock page.** That reference file (CLAUDE.md-documented as the design source of truth, tokens already
   in `src/styles/tokens/`) depicts a floating multi-chat-dock: a row of minimized "chat pills" (avatar +
   name + unread badge) plus one or more simultaneously-open floating chat windows (header with
   avatar+status dot+minimize/close, scrollable bubble history showing the author's avatar/name only on
   an author change, a composer). The current mock's single embedded chat panel is a simplification of
   this, not the target. Build toward the design-reference interaction model; keep the existing rail's
   FRIENDS/REQUESTS/GROUPS/IGNORED tab structure since it matches both the reference and the current
   preview.

═══════════════════════════════════════════════════════════════════════════
THE REAL API SURFACE (verified against shipped backend source, 2026-07-17)
═══════════════════════════════════════════════════════════════════════════

ALL 36 endpoints require authentication (`RequireAuthorization`) — there is no anonymous social surface,
unlike Files' anonymous reads. Base path `/api/social`. Keyset cursors are the plain ULID string of the
last row (simpler than Content's Base64Url cursor) — pass it back verbatim as `?cursor=`.

Friends:
  POST   /friends/requests               { addresseeId }               → send a request
  POST   /friends/requests/{id}/accept                                 → accept
  DELETE /friends/requests/{id}                                        → decline (addressee) or cancel (requester)
  DELETE /friends/{userId}                                             → remove an accepted friend
  GET    /friends?cursor=&limit=                                       → keyset FriendResponse[]
  GET    /friends/requests                                             → { incoming: [...], outgoing: [...] }

Blocks:
  PUT    /blocks/{userId}                                              → block (idempotent)
  DELETE /blocks/{userId}                                              → unblock
  GET    /blocks                                                       → BlockedUserResponse[] (no cursor)

Groups:
  POST   /groups                          { name, description, visibility: "public"|"private" }
  GET    /groups?filter=mine|public|all&cursor=&limit=                 → keyset GroupSummaryResponse[]
  GET    /groups/{id}                                                  → GroupDetailResponse
  PUT    /groups/{id}                     { name, description, visibility }
  DELETE /groups/{id}                                                  → owner only
  GET    /groups/{id}/members?cursor=&limit=                           → keyset GroupMemberResponse[]
  DELETE /groups/{id}/members/{userId}                                 → kick (owner/admin only; NOT self-leave)
  POST   /groups/{id}/join                                             → public groups only
  POST   /groups/{id}/leave                                            → self-leave (owner gets 422 — must transfer/delete first)
  PUT    /groups/{id}/members/{userId}/role   { role: "admin"|"member" }
  PUT    /groups/{id}/owner               { userId }                  → transfer ownership (only way an owner can leave)
  POST   /groups/{id}/invites             { userId }
  GET    /invites                                                     → pending invites addressed to me (GroupInviteResponse[], no cursor)
  POST   /invites/{id}/accept
  DELETE /invites/{id}                                                 → decline (invitee) or cancel (inviter)

Messaging:
  POST   /conversations/direct            { userId }                  → get-or-create a DM; privacy/block gated
  GET    /conversations?limit=                                        → ConversationResponse[] — NO CURSOR, hard-capped at 200,
                                                                          unstable last-activity order (the one deliberate non-keyset list)
  GET    /conversations/{id}/messages?cursor=&limit=                   → keyset MessageResponse[], newest-first
  POST   /conversations/{id}/messages     { body }                    → send (max 4000 chars, markdown)
  PUT    /messages/{id}                   { body }                    → edit (sender only)
  DELETE /messages/{id}                                                → sender, or group owner/admin for group chats (tombstones to "[deleted]")
  POST   /conversations/{id}/read                                     → stamp own last-read position (drives MY unread count only — never a receipt to the sender)

Notifications:
  GET    /notifications?cursor=&unreadOnly=&limit=                     → keyset NotificationResponse[]
  POST   /notifications/read              { ids? }                    → absent ids = mark all read
  GET    /notifications/unread-count                                  → { count: number } — closed kind set:
                                                                          friend.request / friend.accepted / group.invite /
                                                                          group.invite.accepted / group.kicked.
                                                                          Message arrivals NEVER create a notification row —
                                                                          the message/DM unread badge is a SEPARATE number,
                                                                          see below.

Privacy:
  GET    /privacy                                                     → { friendRequests, messages, groupInvites, showOnlineStatus }
  PUT    /privacy                         (same shape)                → audience values are EXACTLY "everyone" | "friends" | "no_one"

Presence:
  GET    /presence?userIds=id,id,...      (comma-separated, ≤100)     → [{ userId, status: "online"|"away"|"offline" }]
  POST   /presence/heartbeat              (empty body, 204)           → call every ~30s while active; 2 missed beats ⇒ away/offline

TOPNAV'S TWO EXISTING HARDCODED BADGES ("2" friends-icon, "3" messages-icon, ~lines 266-277) map to TWO
DIFFERENT, INDEPENDENT DATA SOURCES — do not conflate them:
  - Friends/bell badge  ← `GET /notifications/unread-count` (`count`)
  - Messages badge      ← client-side sum of `unreadCount` across every row of `GET /conversations`

Response DTO shapes (author matching TypeScript interfaces in `frontend/src/lib/api/types.ts`):
  FriendResponse            { friendshipId, userId, username, friendsSinceUtc }
  FriendRequestResponse     { friendshipId, requesterId, requesterUsername, addresseeId, addresseeUsername, sentOnUtc }
  BlockedUserResponse       { userId, username, blockedOnUtc }
  GroupSummaryResponse      { groupId, name, description, visibility, ownerId, ownerUsername, memberCount, isMember, createdOnUtc }
  GroupDetailResponse       { ...GroupSummaryResponse fields, isAdmin }
  GroupMemberResponse       { userId, username, joinedOnUtc, isOwner, isAdmin }
  GroupInviteResponse       { inviteId, groupId, groupName, invitedUserId, invitedUserUsername, invitedBy, invitedByUsername, sentOnUtc }
  ConversationResponse      { conversationId, type: "direct"|"group", displayName, otherUserId?, groupId?,
                              lastMessageId?, lastMessagePreview?, lastMessageSenderId?, lastMessageOnUtc?,
                              unreadCount, isMuted }
  MessageResponse           { messageId, conversationId, senderId, senderUsername, body, sentOnUtc, editedOnUtc?, isDeleted }
  NotificationResponse      { notificationId, kind, actorId?, actorUsername?, targetId?, isRead, createdOnUtc }
  PresenceEntry             { userId, status }

UI RULES THESE SHAPES IMPLY (don't skip these, they're real backend invariants, not styling choices):
  - Group `visibility` affects DISCOVERY/JOIN ONLY — a private group's members/chat are exactly as
    invisible to non-members as a public group's; don't gate the UI on visibility beyond the groups list
    and the join button.
  - The owner can never leave or be kicked (backend 422s it) — disable/hide "Leave" for the owner in
    `GroupDetailResponse`/membership UI and offer "Transfer ownership" / "Delete group" instead.
  - `isAdmin` on `GroupMemberResponse`/`GroupDetailResponse` already resolves owner-OR-promoted-admin —
    use it directly for showing admin-only actions (kick, invite, set-role), don't re-derive it.
  - Message edit/delete only reveal their controls for: your own messages (edit+delete), or (delete only)
    if you're admin/owner of a GROUP conversation — never for someone else's DM.
  - A deleted message's `body` already arrives as `"[deleted]"` from the backend (tombstone) — render it
    styled/muted like a removed comment, don't hide the row.

═══════════════════════════════════════════════════════════════════════════
REALTIME WIRING (exact, verified against `Forum.Api/Realtime/RealtimeEventMap.cs` + `ChangeNotification.cs`)
═══════════════════════════════════════════════════════════════════════════

Envelope is UNCHANGED from what Content/Engagement already use — `{ type, entity, id, parentId?,
categoryId? }`, camelCase, absent fields omitted (never present-but-null). Social events always have
`categoryId: null`. New `RealtimeViewKind` values: `"group"`, `"conversation"` (in addition to the
existing `"category"|"thread"|"user"`). New `ChangeNotification.entity` values, EXACTLY these six string
literals (no "conversation" or "friend_request" entity exists — these are the only six):
`"friendship"`, `"group"`, `"group_member"`, `"group_invite"`, `"message"`, `"notification"`.

| Event(s)                                              | entity          | routes it reaches (what you must be subscribed to) |
|--------------------------------------------------------|-----------------|------------------------------------------------------|
| Friend request sent/accepted/declined, friend removed  | `friendship`    | `user:<requesterId>` + `user:<addresseeId>` — i.e. your OWN `user` view, already subscribed for the whole app |
| Group renamed/updated, group deleted                   | `group`         | `group:<groupId>` — subscribe when viewing that group's page |
| Member joined/left a group                             | `group_member`  | `group:<groupId>` |
| Group invite sent/accepted/declined                    | `group_invite`  | `user:<inviteeId>` + `user:<inviterId>` — your own `user` view again, NOT the group view (an invitee isn't a member yet) |
| Message sent/edited/deleted — DIRECT conversation      | `message`       | `conversation:<conversationId>` (open chat) + BOTH participants' `user` views (badge path, even when the chat isn't open) |
| Message sent/edited/deleted — GROUP conversation       | `message`       | `conversation:<conversationId>` (== the group's id) + `group:<groupId>` — **no per-member fan-out**: a member not subscribed to either view will only see the new unread count on next open/resync, not live. This is a documented backend tradeoff, not a bug to route around. |
| A durable notification was created (the bell)          | `notification`  | `user:<recipientId>` only |
| Presence changes                                        | — NOT ON THE BUS AT ALL — | poll `GET /presence`, see above |
| Group created                                            | — NOT WIRED — | the creator already has it from the POST response; nobody else can see an unlisted new group yet |

`parentId` = the container: the conversation id for messages, the group id for group_invite/group_member
events, absent for friendship/notification.

Practical subscription rules:
- Every authenticated user is already subscribed to their own `user:<selfId>` view somewhere in the app
  shell (mirror however the existing bell/`user` subscription is wired today) — friendship, group_invite,
  and notification events all arrive there for free once you extend `invalidation.ts`'s switch and
  `PUSH_COVERED_KEY_ROOTS`. You do NOT need a new subscription for these three.
  the two-participant DM badge case needs no extra subscription either, for the same reason.
- Subscribe to `useRealtimeSubscription("group", groupId)` only while a group's detail/member page is open.
- Subscribe to `useRealtimeSubscription("conversation", conversationId)` only while that chat
  window/pill is open (mirrors the existing `useRealtimeSubscription("thread", threadId)` pattern in
  `frontend/src/app/t/[id]/page.tsx`).
- `frontend/src/lib/realtime/invalidation.ts` — add a `switch` case per new entity, invalidating the
  right `queryKeys.*` root (mirror the existing `"reaction"` case's predicate-based shape for anything
  keyed by more than one id, e.g. invalidate both `queryKeys.groupMembers(groupId)` AND
  `queryKeys.notifications()` where relevant).
- `frontend/src/lib/realtime/notification-href.ts` and `TopNav.tsx`'s `ActivityBell()` /
  `LiveActivityPanel.tsx` rendering both need new cases for the six entities above, or they'll render as
  generic unclickable/unlabeled rows in the live-activity log (they still APPEAR there automatically —
  only the icon/label/link needs new branches).

═══════════════════════════════════════════════════════════════════════════
FILES / MINIO INTEGRATION (exact — the target-type name changed from the original brief)
═══════════════════════════════════════════════════════════════════════════

`FileTargetType` in `frontend/src/lib/api/types.ts` currently has `"dm"` in its union — **this is now
WRONG and must be corrected to `"message"`** (the backend repurposed/renamed the never-used `Dm` stub to
`Message` — no live `"dm"` rows ever existed, so this is a clean rename, not a migration). Also add
`"group_icon"` to the union. Both are now REAL and working (the backend closed the 422 stub AND the
anonymous-read gap): message images are gated to conversation participants on read (an outsider gets 403,
not just a broken image), group icons are anonymous-readable exactly like avatars/category icons (no
gate). Reuse the existing upload pipeline (`frontend/src/lib/upload/upload.ts`, `use-upload-manager.ts`,
`frontend/src/lib/api/files.ts`, `MarkdownEditor`'s `onUploadImage` prop) unchanged — just point it at
`"message"` for the chat composer and build the `GroupIcon` component (structural copy of
`CategoryIcon.tsx`) pointed at `"group_icon"` for group avatars, with the same replace-on-reupload
semantics group owners/admins already get for category icons.

═══════════════════════════════════════════════════════════════════════════
WHAT TO BUILD
═══════════════════════════════════════════════════════════════════════════

- New `frontend/src/components/social/` component set (see architecture section above) + a `use-social.ts`
  hooks module + `socialApi` fetch module (mirror `engagement.ts`) + `queryKeys` additions, all typed
  against the exact DTOs above.
- Replace the mock `/social` page's data layer entirely, restructured toward the design-reference's
  floating multi-chat-dock model: friend list + requests (send/accept/decline/remove) with real presence
  dots, group discovery/detail/members/roles/invites, a real DM+group chat (floating pills + windows per
  the design reference), a privacy-settings section, and a real notification list wired to
  `unread-count`.
- `usePresenceHeartbeat()` hook (visibility-aware ~30s interval) mounted once near the app root (e.g.
  alongside where realtime connection lifecycle already lives) — not per-page.
- Group icon upload/display via the new `GroupIcon` component.

TESTING (required)

- Vitest unit tests mirroring existing precedents (`feed-merge`, `socket-manager`,
  `invalidation-mapping` test files) for new client logic: conversation-list sort/merge (remember it's
  the one no-cursor, cap-200 list), notification/unread-count aggregation, presence-status derivation,
  the two independent TopNav badge sources.
- Manually exercise the golden path in a real browser against the live backend before calling this done
  (per CLAUDE.md's rule: UI changes are verified by actually using the feature) — friend request → accept
  → DM round trip with a live WS push visible in a second browser session, group create → invite → join →
  group message live-pushed to members with the group view open, presence heartbeat flips
  online→away→offline realistically, owner-cannot-leave is enforced in the UI (not just the API).

DOCUMENTATION (required)

- Update `frontend/README.md` — remove the "Social UI-only mock" gap note, add any new gaps found.

Do NOT create git commits or pushes — stage changes only; commits/pushes are the human's job, always.
```
