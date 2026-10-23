# forum-frontend — React SPA (Architecture A, Phase 8)

The decoupled frontend of the thesis forum: a **Next.js (App Router) + TypeScript** single-page
client for the .NET 10 modular-monolith backend in `../backend`. Next.js is used strictly as an
app shell, router and build tool — **every byte of data is fetched by the browser directly from
the .NET API** (React Query + `fetch`); no Next server code ever talks to the backend on the
user's behalf.

## Quickstart

```bash
# 1. infra + backend (repo root)
docker compose up -d                                   # Postgres + RabbitMQ + MinIO
cd backend
dotnet run --project src/Bootstrap/Forum.Api -- migrate
dotnet run --project src/Bootstrap/Forum.Api           # http://localhost:5099

# 2. frontend (this folder)
npm install
npm run dev                                            # http://localhost:3000
```

`.env.example` documents the two environment variables (`NEXT_PUBLIC_API_URL`,
`NEXT_PUBLIC_WS_URL`); the defaults match the backend's launch profile, so no `.env` is needed
for local dev.

| Script | What it does |
| --- | --- |
| `npm run dev` | dev server on :3000 |
| `npm run build` | production build |
| `npm run lint` | ESLint (next/core-web-vitals + TS) |
| `npm run typecheck` | `tsc --noEmit` (strict, `noUncheckedIndexedAccess`) |
| `npm test` | Vitest unit/component suite |
| `npm run format` / `format:check` | Prettier |

## End-to-end tests (Playwright)

`e2e/` renders the **production bundle** in a real browser against the **real API on the
Development seed** — the layer that caught nothing when the anonymous home page broke on the A-12
private-category 404 (backend tests and Vitest never render the SPA). Config: `playwright.config.ts`
(its header is the full recipe); specs: `home-anonymous` (the regression pin), `browse`,
`auth-compose`, `mobile-layout`. Two projects: **`desktop`** (Desktop Chrome) and **`android`**
(Pixel 7 emulation — Chrome-for-Android UA, touch, 412 px at DPR 2.625; the mobile spec runs only
there).

```bash
# 1. the stack (repo root) — once; the suite has no webServer block
make infra-up
RateLimiting__Global__PermitLimit=100000 RateLimiting__Auth__PermitLimit=1000 \
  make api ARGS=--migrate            # :8080 — the raised limits are the backend's test knobs
make seed                            # Development profile (non-empty DB: make seed ARGS=--force)
cd frontend && NEXT_PUBLIC_API_URL=http://localhost:8080 npm run build && npx next start -p 3000

# 2. the suite (this folder)
npm run test:e2e                     # both projects
npm run test:e2e:android             # the phone only
npx playwright show-report           # playwright-report/ (traces + screenshots on failure)
```

No `playwright install` is needed: the config uses the portable `chrome-headless-shell` that
`scripts/lighthouse.sh --bootstrap-chrome` puts in `../.bench/chrome/` (override with
`E2E_CHROME_PATH`). `E2E_BASE_URL` / `E2E_API_URL` retarget the suite (e.g. the cluster's
`https://forum.local`; HTTPS errors are ignored for the mkcert cert). Tests only add rows with
unique titles and never delete, so a run is repeatable; `make seed ARGS=--force` resets the data.
The `mobile-layout` spec also carries two `test.fail`-marked checks that document measured phone
gaps (signed-in TopNav wider than 412 px, sub-40 px secondary tap targets) — they turn into real
failures the day the product fixes them, which is the cue to delete the markers.

## Docker image (Phase 10a)

The production image is a **Next.js standalone server** on `node:22-alpine` (`output:
"standalone"` in `next.config.ts`) — it serves the CSR shell and static assets; the browser
still talks to the .NET API directly. Build from the **repo root** (same build-context
convention as the backend image; `frontend/Dockerfile.dockerignore` filters the context):

```bash
# from the repo root — or `make images` to build backend + frontend together
docker build -f frontend/Dockerfile -t forum-dotnet-web:dev \
  --build-arg NEXT_PUBLIC_API_URL=http://localhost:8080 .
```

**Build ARGs — read this before setting anything:**

- `NEXT_PUBLIC_API_URL` (default `http://forum.local`) — the API **origin only**:
  `scheme://host[:port]`, **never with a `/api` path**. `NEXT_PUBLIC_*` is inlined into the
  JS bundles at build time, and every call site already passes `/api/...`-prefixed paths
  (`src/lib/api/http.ts` does `` `${apiUrl}${path}` ``). A value ending in `/api` turns every
  request into `/api/api/...` and 404s the entire app — this is the #1 way to ship a broken
  frontend image. The image is environment-specific by design; rebuild to retarget.
- `NEXT_PUBLIC_WS_URL` — deliberately **not** a build ARG. When unset, `src/lib/config.ts`
  derives `ws(s)://<api-origin>/api/realtime/ws` from the API URL. One knob, no drift.

**Local smoke test** (backend running on `:8080`, e.g. `scripts/dev-api.sh`):

```bash
docker run --rm -p 3000:3000 forum-dotnet-web:dev
```

Open `http://localhost:3000` and check the browser **network tab**: API calls must hit
`http://localhost:8080/api/...` — if you see `/api/api/...` (doubled prefix) or requests
missing the `/api` segment, the build ARG was wrong; rebuild. The realtime pill should reach
LIVE (WebSocket to `ws://localhost:8080/api/realtime/ws`).

The image runs as the base image's non-root `node` user (uid 1000) and, unlike the chiseled
backend image, has a shell — `docker exec -it <ctr> sh` works. See
`../docs/runbooks/docker-images.md` for tagging and scanning.

## Where things live

```
src/
  app/            routes only (thin) — /, /c/[slug], /t/[id], /u/[userId], /search, /social, /auth
  components/
    ui/           design-system primitives (Button, Input, Badge, Modal, Toast, Skeleton, …)
    layout/       TopNav, PageShell (grid texture), CategorySidebar
    markdown/     sanitized renderer + inline-media resolver
    compose/      thread composer modal, MarkdownEditor, TagPicker, AttachmentWidget
    comments/     CommentSection / CommentNode
    thread/       ThreadCard
    engagement/   ReactionButton
    social/       FriendRow/GroupCard/… row family, GroupDetailPanel, GroupModal,
                  the floating chat dock (ChatDock/ChatWindow/MessageBubble + context),
                  PresenceHeartbeat, ProfileSocialActions
    panels/       side-rail panels (live activity, popular tags, about-…)
  lib/
    api/          typed endpoint modules + the ONE http client + RFC7807 mapping + query keys
    auth/         in-memory token store (single-flight refresh), JWT decode, AuthProvider
    realtime/     WebSocket manager (ticket → connect → resubscribe), invalidation mapping
    feed/         k-way merge for the home "All threads" view
    social/       conversation sort/unread math, presence derivation, notification metadata
    markdown/     media convention + remark plugin + heading extraction
    upload/       initiate → presigned PUT → commit flow + upload state manager
  styles/         design tokens (copied from the design system) + base + animations
design-reference/ the Claude Design mockups + token source (reference only, not app code)
```

## Decisions a maintainer should know

- **Auth**: access token (15 min) lives only in JS memory (`lib/auth/token-store.ts`); the
  refresh token is an httpOnly cookie the browser manages. Any 401 triggers ONE single-flight
  silent refresh + retry; a failed refresh is a hard logout. The provider also refreshes
  proactively ~1 min before expiry.
- **Pagination is keyset-only.** `nextCursor` is opaque and passed back verbatim. There is no
  page-number UI anywhere and no total counts.
- **Home "All threads" is a client-side merge.** The backend's feed endpoint *requires*
  `categoryId` — there is no global feed. `lib/feed/feed-merge.ts` k-way-merges the per-category
  keyset feeds newest-first (pinned split out on ingest), only emitting when every refillable
  source has a buffered head. Unit-tested.
- **Markdown is sanitized client-side** (backend stores raw text): react-markdown (raw HTML is
  never parsed) + rehype-sanitize. The **inline-media convention** — `![caption](image:<fileId>)`
  and `@video(<ref>)` — is documented in `lib/markdown/media-convention.ts`; refs resolve through
  `GET /api/files/{id}` at render time. The sanitizer allows exactly those two pseudo-protocols.
- **Uploads are direct-to-storage** (presigned PUT; bytes never touch the API) with the three
  visible states: uploading (progress) → processing (commit verification) → ready.
- **Realtime**: one WebSocket, fresh single-use 30 s ticket per connection attempt, ref-counted
  subscriptions replayed after every reconnect, and a full active-query resync on every
  (re)connect (pushes carry no payloads). Notifications map to React Query invalidations in
  `lib/realtime/invalidation.ts` — except `thread created`, which feeds the LIVE banner instead
  of silently reordering feeds. The app subscribes the session's own `user:<id>` view for its
  whole lifetime (RealtimeProvider): friendship / group-invite / bell events and the DM
  unread-badge path route only there. Group and conversation views are subscribed by the
  components that show them (GroupDetailPanel, ChatWindow/ChatPill).
- **Social** (`/social` + the floating chat dock): friends/requests/blocks, group
  discovery/detail/roles/invites, unified DM+group chat and the durable notification bell, all
  against `/api/social/*`. Chats live in a global dock (pills + up to 3 floating windows,
  `components/social/chat-dock-context.tsx`) that survives route changes; message bodies are
  markdown with the same `image:<fileId>` inline convention (uploads attach with
  `targetType=message`, participant-gated reads server-side; group icons use `group_icon` with
  replace semantics). The TopNav badges are two independent numbers: bell = unread durable
  notifications, messages = summed per-conversation unread counts. **Presence is poll-only by
  backend design** — a visibility-aware 30 s heartbeat (`PresenceHeartbeat` at the app root)
  plus ONE batched `GET /presence` per view on a 25 s `refetchInterval`; never per-row requests.
  One UI-relevant backend rule: `friendRequests: "friends"` normalizes to `"no_one"` server-side
  (friends need no request), so the privacy form doesn't offer it.
- **Permissions are a UX heuristic only** — edit/delete/pin affordances key off ownership and the
  JWT's global-role claim, but every action handles the server's 403 gracefully.
- **Errors** all pass through `lib/api/problem.ts` (RFC7807 `title`/`code`/`errorType`), with the
  documented inconsistencies handled: empty-body 429 → "slow down" toast, envelope-less 401/403s,
  and the bespoke `{ "error": … }` 400.

## Deliberately mocked / deferred (backend gaps, marked in the UI)

- **Comment-count badges** — never rendered; the feed field is hard-coded 0 server-side.
- **Admin panel** — out of scope for this pass; no nav entry is advertised.
- **Sent group invites** — the API only lists invites addressed to *me* (`GET /api/social/invites`),
  so the inviter has no cancel UI (the invitee's decline or the backend's block-severing cleans
  them up).

Formerly listed here, now real: tag autocomplete + POPULAR TAGS (`GET /api/content/tags`),
the profile activity timeline (`GET /api/content/users/{id}/threads` + `/comments`, merged
client-side in `lib/feed/activity-merge.ts`), category icons (Files `category_icon` target),
category create/edit (`POST`/`PUT /api/content/categories` via the sidebar "+ New category"
and the category header "Edit category" — name/description/visibility/icon in one modal),
thread icons (Files `thread_icon` target — a backend target added alongside this UI; one
image per thread, editable by owner/moderator, shown on the thread page and feed cards with
a category-icon fallback), account settings (`/settings` → the `/api/identity/me/*`
endpoints), and the **Social feature** (`/social`, the chat dock, presence, privacy,
notifications — formerly a zero-fetch UI mock, now fully wired to `Forum.Modules.Social`).
