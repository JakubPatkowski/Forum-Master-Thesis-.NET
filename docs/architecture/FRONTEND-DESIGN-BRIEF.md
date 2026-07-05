# Frontend Design Brief — Phase 8 (React SPA)

**Purpose:** this document briefs a design tool (Claude Design / Fable 5) to generate a
professional visual design + reusable atomic component library for the forum's React SPA.
It is the **single source of truth for backend contracts** — every route, field name, error
shape, pagination format, and WebSocket message below was read directly from the backend
source (`backend/src/...`) on **2026-07-05**, not guessed. If the design needs a data shape
that isn't listed here, treat it as a mock/placeholder and flag it — do not invent a shape,
since the mismatch becomes real integration work later.

**Workflow this document supports:** design produced from this brief (in Claude Design, on
Windows, with this repo + `JakubPatkowski/Python-Forum-API` attached as codebase references)
is brought back and integrated into the actual React app with Claude Code on Ubuntu WSL —
real fetch calls, React Query, WebSocket client, wired against the routes below.

---

## 1. Brand & Visual Direction

- **Tone:** modern, subtly cyberpunk — a tech/science/programming-adjacent community forum.
  Not over the top. Reference screenshot: `FORUM://ANGLERS` — bracketed nav labels
  (`01 FORUM`, `02 FISHING MAP`), monospace accents, faint grid/scanline texture, sharp
  cyan/white-on-black palette.
- **Reference codebase for style calibration only:** `JakubPatkowski/Python-Forum-API`
  (production fishing forum) — use for structural/stylistic inspiration. It is **not** an API
  contract source; this document is.
- **Theme:** dark-only for now, with a **neon-orange accent**. Build colors/spacing/radius/
  type as CSS custom properties (design tokens) from day one so a future light theme is a
  token swap, not a rewrite. No theme-switcher UI needed yet.
- **Typography:** headline-forward type scale. Body content is **raw Markdown** (see §4.3 —
  `Thread.Body`/`Comment.Body` are plain strings with no markdown/HTML distinction enforced
  server-side; CLAUDE.md confirms threads store "markdown raw"). The frontend must render
  body text through a Markdown renderer with raw HTML disabled/sanitized — the backend does
  **no sanitization**, so unrendered raw HTML would be a stored-XSS vector against other
  readers. Design the typography scale assuming rendered Markdown output (headings, lists,
  code blocks, links, blockquotes, inline code) — not free-form rich text/WYSIWYG.
- **Iconography:** filled style.
- **Motion:** professional, purposeful — hover/focus states, and a deliberate **"pulse"/
  highlight animation for realtime-arriving content** (new-thread banner, live reaction-count
  bump, WS connection-status indicator). This is a first-class part of the identity, not a
  decoration — the backend pushes real-time change notifications (§4.6) and the UI should
  visibly, calmly acknowledge them.
- **Layout system:** grid-based.
- **Corners:** sharp (no/minimal border-radius).
- Anything under this brand direction not explicitly specified — Fable 5's call.

## 2. Layout & Information Architecture

- **Top nav** as primary navigation.
- **Left + right auxiliary panels on most pages** — e.g. category list on the home page, a
  post table-of-contents on the thread page, user stats on the profile page. Exact panel
  content per page not otherwise specified here is Fable 5's call.
- **Atomic component philosophy — mandatory:** build small reusable primitives first
  (buttons, inputs, badges, tag chips, cards) and compose page templates from them. Do not
  design any page as a one-off monolith.
- **Page inventory** (derived from the API surface in §4 — that section is authoritative for
  what each page needs to fetch):
  - Home / feed — category list + featured/pinned threads + latest threads
  - Category page — thread feed scoped to one category, most-popular-thread callout
  - Thread detail — body (rendered Markdown), nested comment tree, reactions, tags,
    attachments
  - Create/Edit thread — title, body editor, tag picker (see §4.4 gotcha), category select
  - Comment composer — inline, reply-in-place, depth-capped at 5 (§4.5)
  - Profile page (own & others) — stats (thread/comment count, karma), avatar, recent
    activity
  - Search results
  - Auth — login, register
  - Admin/moderation panel — user list, role assignment, ACL entry, block/unblock
  - Notifications / realtime activity indicator (ties into §4.6)
  - File/image upload widget — used inside thread/comment composer, avatar picker, category
    icon picker
  - Error states — 404, 403, generic error boundary
- **Responsive:** mobile/tablet/desktop. Nested comment threads must degrade gracefully on
  narrow viewports — exact technique (indent cap + horizontal scroll fallback vs.
  collapse-to-card) is Fable 5's call.

## 3. Explicit Non-Goals

- **No i18n / language switcher.** (The reference screenshot shows a PL/EN toggle — this
  project does not need one.)
- **No dedicated accessibility pass** (WCAG contrast/keyboard-nav audit) at this stage.
- **No light theme deliverable now** — just keep tokens swappable for later.

---

## 4. Backend Contract Reference (source of truth — read this before designing any data-bound screen)

All JSON is camelCase (ASP.NET Core minimal-API default). All IDs are **ULIDs**, serialized
as plain 26-character strings. All timestamps are ISO-8601 `DateTimeOffset`.

### 4.1 Error envelope (applies to every endpoint below)

Every failure returns an RFC7807 ProblemDetails body. The **actual wire shape** (not what
older project notes say):

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Comments may only nest 5 levels deep.",
  "status": 422,
  "code": "comment.max_depth_exceeded",
  "errorType": "Validation"
}
```

- Read the human message from **`title`**, the stable machine code from **`code`**, and the
  category from **`errorType`** — one of `NotFound | Forbidden | Unauthorized | Conflict |
  Validation | Failure`.
- Status-code mapping: `NotFound→404, Forbidden→403, Unauthorized→401, Conflict→409,
  Validation→422 (used for basically all "bad input", not 400), Failure/other→400`.
- **Known inconsistencies to design around** (build a generic fallback error UI for these):
  - A few auth/admin edge cases return **no `code`/`errorType`** at all (e.g. missing-
    permission 403s from the admin-route filter, logout-all's anonymous-guard 401).
  - One admin endpoint (bad ACL `scopeId`) returns a bespoke `{ "error": "Invalid scope id." }`
    with **400**, not the standard envelope.
  - **429 Too Many Requests has an empty body** — no envelope at all. Show a generic
    "slow down, try again" toast keyed off the status code alone.

### 4.2 Auth & Identity (`/api/identity/*`)

| Action | Route | Auth | Request | Response |
|---|---|---|---|---|
| Register | `POST /api/identity/register` | anon (rate-limited) | `{ username, email, displayName, password }` | 201 `{ userId }` |
| Login | `POST /api/identity/login` | anon (rate-limited) | `{ email, password }` | 200 `{ accessToken, expiresOnUtc }` + sets `refresh_token` cookie |
| Refresh | `POST /api/identity/refresh` | cookie only, no body | — | 200 `{ accessToken, expiresOnUtc }` + rotates cookie; 401 on any invalid/expired/reused token |
| Logout | `POST /api/identity/logout` | cookie only | — | 204 (idempotent, always succeeds) |
| Logout-all | `POST /api/identity/logout-all` | bearer required | — | 204 |
| Admin: list users | `GET /api/identity/admin/users?cursor=&limit=` | `manage` permission | — | 200 keyset page of user summaries |
| Admin: assign/revoke role | `PATCH /api/identity/admin/users/{id}/roles` | `manage` permission | `{ role, assign: bool }` | 204 |
| Admin: add ACL entry | `POST /api/identity/admin/users/{id}/acl` | `manage` permission | `{ scope: "global"\|"category"\|"thread", scopeId, allowBits, denyBits }` | 204 |
| Admin: block/unblock | `PATCH /api/identity/admin/users/{id}/status` | `manage` permission | `{ block: bool }` | 204 |

**Auth token handling (design + integration implications):**
- Access token: **15 minutes**, lives only in JS memory (never localStorage/sessionStorage).
  Attach as `Authorization: Bearer <token>` on every authenticated call.
- Refresh token: **14 days**, httpOnly cookie named `refresh_token`, scoped to path
  `/api/identity`, `SameSite=Strict` — never touched by JS.
- On any `401`, attempt one silent `POST /refresh`, retry the original call once; if refresh
  also fails, force logout → redirect to login. There is no way to distinguish "expired" vs.
  "reused/stolen" vs. "garbage" refresh token from the response — treat all refresh failures
  identically (hard logout).
- **Roles:** global `user` / `moderator` / `admin`, **plus** per-category ACL grants (a user
  can be a moderator scoped to just one category without being a global moderator). The UI
  must not hardcode "if role === moderator" checks — permission is resolved server-side per
  action. Drive edit/delete/pin visibility from **ownership** (`currentUser.id === ownerId`)
  as the client-side heuristic, and always handle a **403 on the actual action** gracefully
  (the button being shown is not a security boundary, just a UX nicety).

### 4.3 Content — Categories (`/api/content/categories`)

| Action | Route | Auth | Notes |
|---|---|---|---|
| Create | `POST /api/content/categories` | `create` permission (global) | `{ slug, name, description?, visibility? }` → 201 `{ categoryId, slug }` |
| List | `GET /api/content/categories` | anon | no pagination, no visibility filter (see gotcha below) |
| Get by slug | `GET /api/content/categories/{slug}` | anon | 404 if missing |
| Update | `PUT /api/content/categories/{slug}` | owner-or-moderator | `{ name, description?, visibility? }` → 204 |
| Delete | `DELETE /api/content/categories/{slug}` | owner-or-moderator | 204 |

**Category resource shape:**
```json
{
  "id": "01...", "slug": "fly-fishing", "name": "Fly Fishing",
  "description": "...", "visibility": "public" | "private",
  "ownerId": "01...", "iconFileId": null, "createdOnUtc": "2026-07-05T..."
}
```
`iconFileId` exists in the shape but is **never set by any current endpoint** — always
`null` today (a Phase 6+ wiring gap, not a design concern; design the icon slot, expect it
empty for now).

**Gotcha — private categories are not hidden from anonymous reads.** `visibility: "private"`
today only gates **who may create a thread** inside it. List/get/feed/search all return
private categories and their threads to anyone. **Do not** design a "hide private content"
filter as if it were a security boundary — it isn't enforced there yet. Do show a
private/public badge (it's true metadata), just don't build client-side access control
around it.

### 4.4 Content — Threads (`/api/content/threads`, `/api/content/search`)

| Action | Route | Auth |
|---|---|---|
| Create | `POST /api/content/threads` | authenticated + `create` at category scope |
| Feed | `GET /api/content/threads?categoryId=&cursor=&limit=` | anon |
| Get one | `GET /api/content/threads/{id}` | anon |
| Update | `PUT /api/content/threads/{id}` | owner-or-moderator |
| Delete | `DELETE /api/content/threads/{id}` | owner-or-moderator |
| Change category | `PATCH /api/content/threads/{id}/category` | moderator of current category only (owner alone cannot move a thread) |
| Pin/unpin | `POST /api/content/threads/{id}/pin` | moderator of category only |
| Search | `GET /api/content/search?q=&cursor=&limit=` | anon |

**Create request:** `{ categoryId, title, body, tagSlugs?: string[] }` (max 5 tags, each
lowercase-kebab-case, ≤32 chars) → 201 `{ threadId }`.

**Feed/search item shape** (`ThreadFeedItemResponse` — identical for both endpoints):
```json
{
  "id": "01...", "categoryId": "01...", "categorySlug": "fly-fishing",
  "categoryName": "Fly Fishing", "title": "...", "isPinned": false,
  "ownerId": "01...", "username": "...", "displayName": "...",
  "likeCount": 0, "commentCount": 0,
  "createdOnUtc": "...", "lastModifiedOnUtc": null
}
```
**Gotcha — `likeCount` and `commentCount` on this shape are hard-coded `0` in SQL today**
(Engagement counts are a separate later join that hasn't been wired into this view). Real
like counts must come from Engagement's batch endpoint (§4.7); **there is no way to get real
per-thread comment counts in a feed-list request today** — no batch endpoint exists for it.
**Decision for this design pass:** omit the comment-count badge from feed/search cards for
v1, or show it only once a corresponding backend endpoint is added later. Design the card
slot so it degrades gracefully either way (e.g. render nothing rather than a misleading "0").

**Thread detail shape** (`ThreadDetailResponse`, `GET /api/content/threads/{id}`):
```json
{
  "id": "01...", "categoryId": "01...", "categorySlug": "...", "categoryName": "...",
  "title": "...", "body": "raw markdown string", "isPinned": false,
  "ownerId": "01...", "username": "...", "displayName": "...",
  "tags": ["fly-fishing", "dunajec"],
  "createdOnUtc": "...", "lastModifiedOnUtc": null
}
```
A soft-deleted thread simply 404s — there is no "[deleted]" placeholder for threads (unlike
comments, §4.5).

**Pagination — keyset/cursor, never numbered pages.** `GET` feed/search accept an opaque
`cursor` query param and return:
```json
{ "items": [...], "nextCursor": "opaque-base64url-string-or-null", "hasMore": true }
```
Treat `nextCursor` as a black box — never parse or construct it client-side. Always design
"Load more" / infinite-scroll, **never** page-number controls, and never assume a total
count exists (it's never returned). A malformed/garbage cursor value returns **422**, not a
silent empty page.

### 4.5 Content — Comments (`/api/content/threads/{threadId}/comments`, `/api/content/comments/{id}`)

| Action | Route | Auth |
|---|---|---|
| Create | `POST /api/content/threads/{threadId}/comments` | authenticated + `comment` at category scope |
| Get tree | `GET /api/content/threads/{threadId}/comments` | anon |
| Update | `PUT /api/content/comments/{id}` | owner-or-moderator |
| Delete | `DELETE /api/content/comments/{id}` | owner-or-moderator |

**Create request:** `{ parentId: string|null, body: string }` (max 10,000 chars) → 201
`{ commentId }`.

**Comment tree item shape** (`CommentResponse`, ordered depth-first by `path`):
```json
{
  "id": "01...", "threadId": "01...", "parentId": "01..." | null,
  "path": "01A.01B", "depth": 2, "body": "...", "isDeleted": false,
  "ownerId": "01...", "username": "...", "displayName": "...",
  "createdOnUtc": "..."
}
```
- **Max depth is 5** (root = depth 0, replies go to depth 1–5). Replying to a depth-5 comment
  returns **422 `comment.max_depth_exceeded`** — disable the "Reply" affordance once
  `depth === 5` in the UI, don't rely on the error alone.
- **Soft-delete:** a deleted comment's `body` becomes the literal string `"[deleted]"` and
  `isDeleted: true` — the row **stays in the tree**, its children remain nested underneath it
  unchanged, and its author fields are still returned as-is (no anonymization). Design the
  comment-tree connecting-lines component to keep rendering children under a `"[deleted]"`
  parent normally, just with different (greyed/placeholder) body styling for that one node.
- Replying to a comment in a *different* thread than the one in the URL is a **422**
  (guards against a stale/copy-pasted parent id), checked before the permission gate.

### 4.6 Tags — no listing/autocomplete endpoint exists yet

Tag get-or-create happens **only** as a side effect of creating a thread
(`tagSlugs: string[]` on `POST /api/content/threads`). There is currently **no** `GET` endpoint
to list or search existing tags. Matching against existing tags at creation time is
**case-sensitive exact string match** — no normalization beyond the client-enforced
lowercase-kebab-case validation regex (`^[a-z0-9]+(?:-[a-z0-9]+)*$`).

**Decision (backend gap, resolved per this brief's own call):** design the tag picker with
autocomplete/suggestions from existing tags as requested (§ design preferences) — but this
requires a **new backend endpoint** (e.g. `GET /api/content/tags?query=`) that does not exist
today. **Design should mock this with placeholder tag data** matching the shape
`{ slug: string, name: string }`; Claude Code will add the real endpoint (with proper
case-insensitive matching) during integration. Enforce lowercase-kebab-case client-side
before submit either way, to match the existing validation regex.

### 4.7 Files / Uploads (`/api/files/*`)

Two-phase, direct-to-storage flow (bytes never pass through the API):

| Step | Route | Notes |
|---|---|---|
| 1. Initiate | `POST /api/files` `{ contentType, sizeBytes }` | 201 `{ fileId, objectKey, uploadUrl, method: "PUT", expiresOnUtc }` |
| 2. Upload | client `PUT`s raw bytes to `uploadUrl` directly | not part of this API |
| 3. Commit | `POST /api/files/{fileId}/commit` (no body) | 200 `{ fileId, contentType, sizeBytes, width, height }` — **real, sniffed values**, not the declared ones |
| Get | `GET /api/files/{fileId}` | anon; 200 `{ fileId, url, contentType, sizeBytes, width, height, expiresOnUtc }`; pending (uncommitted) files 404 |
| List by target | `GET /api/files?targetType=&targetId=` | anon; same shape as above, as an array |
| Attach | `POST /api/files/{fileId}/attachments` `{ targetType, targetId? }` | 204 |
| Detach | `DELETE /api/files/{fileId}/attachments?targetType=&targetId=` | 204 |

- Allowed types: `image/png, image/jpeg, image/gif, image/webp`. Max size: **5 MiB**.
- `targetType` values: `thread`/`comment` (**additive**, capped at 10 attachments per
  target), `avatar`/`category_icon` (**replace** — attaching a new one detaches the old),
  `dm` (rejected, 422 — not built yet).
- Design the upload widget with three visible states: **uploading** (progress bar during the
  raw `PUT`), **processing** (during commit — dimensions/type are being verified server-side),
  **ready** (final preview). A **422 `file.size_mismatch` / `file.type_mismatch`** at commit
  means the declared metadata lied about the actual bytes — show a clear "file doesn't match
  what was declared, try again" error, not a generic failure.

### 4.8 Engagement — Reactions & Stats (`/api/engagement/*`)

| Action | Route | Auth |
|---|---|---|
| Like (toggle on) | `PUT /api/engagement/reactions/{targetType}/{targetId}` | authenticated |
| Unlike (toggle off) | `DELETE /api/engagement/reactions/{targetType}/{targetId}` | authenticated |
| Single summary | `GET /api/engagement/reactions/{targetType}/{targetId}` | anon |
| Batch summary | `GET /api/engagement/reactions/batch?targetType=&targetIds=id1,id2,...` | anon (max 100 ids) |
| User stats | `GET /api/engagement/users/{userId}/stats` | anon |

- `targetType` is `thread` or `comment` only.
- Every reaction call (PUT/DELETE/GET single) returns the **same shape**:
  `{ "targetId": "01...", "count": 3, "viewerReacted": true }`.
- **Both toggle directions are idempotent** — re-liking an already-liked target, or unliking
  something never liked, both return **200 with the current (unchanged) summary**, never an
  error. Design the like button as an instant, optimistic toggle — no confirmation, no error
  state for the common case.
- Batch summary returns a JSON array of the same per-item shape, one entry per **distinct**
  requested id, zero-filled (`count:0, viewerReacted:false`) for ids with no reactions —
  **there is no existence check**, so use this purely to hydrate like-counts onto a feed you
  already fetched, not to validate that ids exist.
- **User stats shape:** `{ userId, username, displayName, threadCount, commentCount, karma }`
  — 404 only if the user id itself doesn't exist; a real user with zero content still
  returns 200 with all-zero counts.

### 4.9 Realtime — WebSocket (`/api/realtime/*`)

**Connection flow:**
1. While already authenticated (bearer token), call `POST /api/realtime/ticket` → 200
   `{ "ticket": "<jwt>", "expiresInSeconds": 30 }`.
2. **Immediately** open a WebSocket to `GET /api/realtime/ws?ticket=<ticket>` — the ticket is
   **single-use and expires in 30 seconds**, so mint it right before connecting; never cache
   or reuse it.
3. Any connection failure (bad/expired/replayed ticket, malformed value) surfaces only as a
   generic socket error/close from the browser's perspective — there is no way to introspect
   *why* it failed. **On any failed/dropped connection, the client's only correct move is:
   mint a fresh ticket and reconnect** — do not attempt to distinguish failure reasons.

**Client → server messages:**
```json
{ "action": "subscribe" | "unsubscribe", "view": "category" | "thread" | "user", "id": "01..." }
```
- `view: "user"` may **only** target the connection's own user id — subscribing to another
  user's view returns a `forbidden-view` error.
- Max **64 subscriptions per connection** (ample for normal navigation; don't subscribe
  per-visible-card, subscribe per-open-view).

**Server → client acks/errors:**
```json
{ "type": "subscribed" | "unsubscribed", "view": "...", "id": "..." }
{ "type": "error", "reason": "unknown-view" | "unknown-action" | "malformed-message" | "forbidden-view" | "too-many-subscriptions", "view": "...", "id": "..." }
```

**Server → client change notifications** (no content, ever — always re-fetch and patch):
```json
{ "type": "created" | "updated" | "deleted", "entity": "thread" | "comment" | "reaction", "id": "01...", "parentId": "01..." | null, "categoryId": "01..." }
```
- `entity: "thread"` — `id` = thread id, `parentId` = null.
- `entity: "comment"` — `id` = comment id, `parentId` = containing thread id.
- `entity: "reaction"` — `id` = the **reacted-to** thread/comment (not a reaction-record id),
  `parentId` = containing thread id (null if the target is itself a thread). Reaction events
  are **also** pushed to the acting user's own `"user"` view (multi-device like-state sync) —
  design a subtle re-sync pulse for "your like state changed elsewhere."
- Fired for: thread created/updated/deleted, comment created/updated/deleted, reaction
  added/removed. Never carries title/body/author — the envelope's only job is "go re-fetch
  this," matching the fetch-then-patch architecture (ADR 0010).

**UI subscription pattern:** subscribe to `"category"` view on category/home pages, `"thread"`
view on the thread detail page, `"user"` view on the current user's own profile. On a
matching notification, re-fetch the affected resource and show the **pulse/highlight
animation** from §1 rather than silently swapping content under the reader — e.g. a "new
thread was posted, click to load" banner on the feed instead of an unannounced reorder.

---

## 5. Component Library Scope

Buttons (primary/secondary/ghost/danger, all states: default/hover/focus/active/disabled/
loading), inputs, badges (role, visibility, pinned), tag chips with autocomplete dropdown,
cards (thread card, comment node, category card, user card), avatar, markdown renderer
component, upload widget (progress → processing → ready), "Load more" / infinite-scroll
trigger, toasts, modals, tabs, dropdown menus, tooltips, WS connection-status indicator,
live-update pulse/banner component, skeleton loaders (per-section, not full-page), empty-state
slots, and error-state components mapped explicitly to 404 / 403 / 422 (inline field errors) /
429 (generic "slow down" toast) / unknown.

## 6. Data & State UX Checklist

- Skeletons per section while loading, never a full-page spinner.
- Empty states: no categories, no threads in a category, no comments, no search results.
- Ownership-aware actions: show edit/delete/pin controls based on
  `currentUser.id === ownerId` as a UX heuristic only; always handle a 403 from the actual
  action gracefully (permission is truly resolved server-side, see §4.2).
- Soft-deleted comments render `"[deleted]"` inline, children stay nested and interactive.
- Disable "Reply" at comment depth 5; still allow reading replies-of-replies up to that cap.
- Pinned threads sort first and are visually distinguished.
- Private-category badge is metadata only — not a client-side visibility filter (§4.3).
- Pagination is always cursor-based "Load more" — never numbered pages, never a total count.
- Like button: optimistic, instant, idempotent both directions (§4.8) — no confirm dialog.
- A WS notification never contains content — always re-fetch, and visibly (not silently)
  patch the view with the pulse/banner treatment from §1.

## 7. Handoff Format

- Deliver: design tokens (CSS custom properties for color/spacing/radius=sharp/shadow/font
  scale) + component specs covering every state listed in §5 + page compositions for the
  inventory in §2.
- **Naming:** component names should map 1:1 to eventual React component names — PascalCase,
  domain-prefixed (`ThreadCard`, `CommentNode`, `CategorySidebar`, `ReactionButton`,
  `TagPicker`, `RealtimeStatusBadge`, `MarkdownRenderer`, etc.).
- Use **realistic mock data matching the exact shapes in §4** (field names and types) so
  swapping in real API calls during integration is a pure data-source change, not a component
  rewrite.
