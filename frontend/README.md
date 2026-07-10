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
    panels/       side-rail panels (live activity, popular tags, about-…)
  lib/
    api/          typed endpoint modules + the ONE http client + RFC7807 mapping + query keys
    auth/         in-memory token store (single-flight refresh), JWT decode, AuthProvider
    realtime/     WebSocket manager (ticket → connect → resubscribe), invalidation mapping
    feed/         k-way merge for the home "All threads" view
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
  of silently reordering feeds.
- **Permissions are a UX heuristic only** — edit/delete/pin affordances key off ownership and the
  JWT's global-role claim, but every action handles the server's 403 gracefully.
- **Errors** all pass through `lib/api/problem.ts` (RFC7807 `title`/`code`/`errorType`), with the
  documented inconsistencies handled: empty-body 429 → "slow down" toast, envelope-less 401/403s,
  and the bespoke `{ "error": … }` 400.

## Deliberately mocked / deferred (backend gaps, marked in the UI)

- **Social page** (`/social`) — UI-only preview with local state; the backend has no Social
  module. A persistent PREVIEW banner says so.
- **Comment-count badges** — never rendered; the feed field is hard-coded 0 server-side.
- **Admin panel** — out of scope for this pass; no nav entry is advertised.

Formerly listed here, now real: tag autocomplete + POPULAR TAGS (`GET /api/content/tags`),
the profile activity timeline (`GET /api/content/users/{id}/threads` + `/comments`, merged
client-side in `lib/feed/activity-merge.ts`), category icons (Files `category_icon` target),
category create/edit (`POST`/`PUT /api/content/categories` via the sidebar "+ New category"
and the category header "Edit category" — name/description/visibility/icon in one modal),
thread icons (Files `thread_icon` target — a backend target added alongside this UI; one
image per thread, editable by owner/moderator, shown on the thread page and feed cards with
a category-icon fallback), and account settings (`/settings` → the `/api/identity/me/*`
endpoints).
