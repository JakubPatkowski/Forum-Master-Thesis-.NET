# Architecture A — Phased Implementation Plan

> **Status:** authoritative roadmap · **Updated:** 2026-06-24 · **Language:** English (repo convention)
> **Read first:** [`REQUIREMENTS-AND-ASSUMPTIONS.md`](./REQUIREMENTS-AND-ASSUMPTIONS.md),
> [`DOMAIN-MODEL-AND-DATABASE.md`](./DOMAIN-MODEL-AND-DATABASE.md), [`../db/permissions-acl-design.md`](../db/permissions-acl-design.md), ADRs 0002–0010.
>
> **How to use this with Claude.** Each phase is self-contained: **Goal · Depends on · Steps · Watch out ·
> Definition of Done · Events/contracts · START-OF-PHASE REMINDERS.** When you open a session, say e.g.
> *"We finished Phase 2, start Phase 3 — read its START-OF-PHASE REMINDERS first."* Claude should re-read the
> referenced docs + the phase block before writing code, and must keep `Forum.ArchitectureTests` green.

---

## Global rules (apply in EVERY phase — never regress)

- **Result pattern**, no exceptions for expected failures; map to HTTP at the edge; validation order **404 → 403 → 422**.
- **ULID** for every id; **audit interceptor** stamps `created/last_modified_*`; **soft-delete** filter on removable aggregates; **ownership** via `owner_id`.
- **Reads:** SQL views + **keyset** pagination (never OFFSET, never N+1). **Writes:** aggregates + repository + UnitOfWork.
- **Module isolation:** code crosses a module only via `*.Contracts` + integration events; **no cross-schema DB FK**; everything `internal` except `Contracts/`. Keep `Forum.ArchitectureTests` passing — it's the guardrail.
- **CPM:** never put `Version=` in a `.csproj`; add to `backend/Directory.Packages.props`.
- **Secrets:** `dotnet user-secrets` (dev) / k8s Secret (cluster). Never in `appsettings*.json` or git.
- **Migrations** are per-module and applied by the **k8s Job** (ADR 0005), never at pod startup.
- **Language:** English code/identifiers/comments; thesis prose Polish.
- **Sandbox/mount-lag:** trust the Read/Edit/Write tool result. After writing a file, do **not** re-read it to "verify" or re-check for truncation — the tool fails loudly on error. Use the Read tool, not `cat`, if a read is genuinely needed.
- **Definition of Done for every phase includes:** `dotnet build` + `dotnet test` green (incl. ArchitectureTests), `dotnet format --verify-no-changes`, and the new endpoints documented in OpenAPI.

---

## Phases at a glance

| # | Phase | Outcome | Key risk |
|---|---|---|---|
| 0 | Foundation & build-green | Solution builds, boots, health OK; SharedKernel/Common/Infra real | CPM version drift; over-building before slices |
| 1 | Identity + Authz | Users, JWT+refresh rotation, Argon2id, RBAC + SQL bitmask ACL | login leakage; ACL SQL; refresh reuse-detection |
| 2 | Content | Categories, threads, **nested comments**, tags, FTS, keyset | materialized path; soft-delete filters; keyset cursor |
| 3 | Files | Direct-to-MinIO presigned upload + attachments | trusting declared content-type; orphan sweep; CORS |
| 4 | Engagement | Reactions + stats view + counters | counter strategy (avoid full scans); toggle semantics |
| 5 | Social *(OPTIONAL)* | Friends + text DMs | only if B also builds it; otherwise skip |
| 6 | Messaging backbone | RabbitMQ + outbox relay + idempotent consumers | outbox atomicity; idempotency; readiness gating |
| 7 | Real-time WebSocket | Change-notification fan-out; SPA patches | visibility on push; reconnect/resync |
| 8 | Frontend (React SPA) | Auth, pages, React Query, WS patching, upload | token storage; 401 refresh; keyset infinite scroll |
| 9 | Seed + benchmark + observability | Deterministic seed, k6 scenarios, Grafana, dashboards | parity with B (seed/limits/isolation) |
| 10 | k8s deploy + hardening + run | Full stack on minikube; comparative benchmark | migration Job order; pool ≤ max_connections |

> Vertical-slice tip: after Phase 2 you may build a **thin** end-to-end UI slice (login + feed) to validate the
> stack before the full frontend (Phase 8). Phases 6–7 (bus/WS) can be brought forward if real-time is needed
> earlier, but the modules already write to their outbox tables during their own phase, so deferring is cheap.

---

## Phase 0 — Foundation & build-green

**Goal.** Turn the scaffold into a real, building, bootable skeleton: shared building blocks + Host wiring +
test harness, with no domain yet.

**Depends on.** Nothing (current scaffold).

**Steps.**
1. `dotnet restore Forum.slnx`; resolve any centrally-pinned NuGet version issues in `Directory.Packages.props`.
2. **Forum.SharedKernel:** `Entity`, `AggregateRoot` (domain-event collection + `Raise`), `Ulid` usage/value
   conversions, `Result`/`Result<T>`/`Error`/`ErrorType`. Add audit fields to `AggregateRoot` base + an
   `ISoftDeletable`/`IOwned` marker.
3. **Forum.Common:** `ICommand/IQuery` + handler interfaces, `IModule`/`IEndpoint` + mapping, in-process
   `IEventBus`, paging (keyset cursor helpers), `ICorrelationContext`.
4. **Forum.Infrastructure:** EF `DbContext` base (`SaveChangesAndDispatchEventsAsync`, snake_case naming,
   NoTracking default for reads, audit interceptor), **outbox** base + table convention, RabbitMQ connection
   wrapper (not wired to consumers yet), MinIO client wrapper, ordered **startup task** runner.
5. **Forum.Api (Bootstrap):** Program.cs wiring — module discovery (`IModule`), auth skeleton (JWT bearer +
   cookie), middleware (correlation-id, exception→problem-details, security headers, CORS allow-list, rate
   limiting), OpenTelemetry (ASP.NET/EF/Npgsql/HttpClient), Serilog→console/Loki, health endpoints, OpenAPI,
   the `migrate` argument hook (no-op until Phase 1 adds a DbContext).
6. **Tests:** `Forum.ArchitectureTests` rules (module boundaries + Domain purity), `TestUtilities`
   (Testcontainers Postgres fixture), a smoke `IntegrationTests` that boots the Host and hits `/health/live`.

**Watch out.** Don't build domain logic yet. Keep `Forum.Api` the only executable. Verify CPM pins actually
restore. The audit interceptor + soft-delete query filter must be in the base so every later module inherits them.

**Definition of Done.** `dotnet build`/`test`/`format` green; app boots; `/health/live` returns 200; OpenAPI loads;
ArchitectureTests pass with at least the boundary + purity rules active.

**START-OF-PHASE REMINDERS.**
- *Remember:* shared base types are load-bearing — get `AggregateRoot` (events + audit), `Result`, keyset paging
  and the EF base (audit interceptor + soft-delete filter + event dispatch) right now, because every module
  inherits them. Don't add a second event mechanism later. CPM only. Health/observability skeleton before domain.

---

## Phase 1 — Identity + Authz (the keystone)

**Goal.** Accounts, sessions, and the **RBAC + bitmask ACL** authorization that everything else gates on.

**Depends on.** Phase 0.

**Steps.**
1. **Schema `forum_identity`:** `users` (email, username_lc unique, Argon2id `password_hash`, status, avatar
   logical ref, audit), `refresh_tokens` (family_id, token **hash**, status, rotated_to, expiry). DbContext +
   migration.
2. **Schema `forum_authz`:** `actions`, `roles`, `user_roles`, `acl_entries (scope, scope_id, principal,
   allow_bits, deny_bits)`, `effective_perm_cache`; the `int_or_agg` aggregate + `effective_mask()` resolver +
   indexes — shipped as **raw-SQL EF migrations** in Identity's `Infrastructure/Acl/` (ADR 0004). Seed roles
   `user/moderator/admin` and the action→bit catalog.
3. **Argon2id** hasher (ADR 0007) with constant-time verify + dummy-verify on miss.
4. **Auth flows:** register, login, **refresh with rotation + reuse-detection** (reused/revoked token revokes the
   whole `family_id`), logout, logout-all. Access JWT (15 min) in response/JS memory; refresh (14 d) in
   **httpOnly cookie**. Per-IP rate-limit on login/register.
5. **AuthZ surface:** `ICurrentUser` (resolves identity + correlation), `RequirePermission("thread.create")`
   endpoint filter calling the SQL resolver / cache; ownership helpers. Admin endpoints: list users, assign/
   revoke roles, permission override (allow/deny ACL), block/unblock.
6. **Per-context roles:** model a category/group moderator as an `acl_entries` row at `scope='category'`.
7. **Events (to outbox table now; relayed in Phase 6):** `UserRegistered`, `UserBlocked`, `RoleAssigned`,
   `AclEntryChanged` (the last two enqueue an `effective_perm_cache` recompute).
8. **Tests:** `Modules.Identity.Tests` (hashing, rotation/reuse-detection, resolver math), integration test of
   the ACL SQL against real Postgres.

**Watch out.** **Never reveal which half of a login failed** — collapse to one error, constant time. Refresh
token stored **hashed** only. Reuse-detection must revoke the family, not just the token. Apply ACL SQL via the
**migration Job**. Keep `404 → 403 → 422`. Cache invalidation on role/ACL change is event-driven — wire the
enqueue now even though the relay lands in Phase 6 (process synchronously in-proc until then if needed).

**Definition of Done.** Register/login/refresh/logout work end-to-end; a protected endpoint correctly returns
403 without permission and 200 with it; reuse of a rotated refresh token revokes the family; ACL resolver
returns correct effective masks in an integration test.

**START-OF-PHASE REMINDERS.**
- *Remember:* this is the security keystone — login must not leak account existence (constant time + single
  error), refresh tokens are **hashed** with **rotation + reuse-detection** (theft revokes the family), passwords
  are **Argon2id**, and the ACL functions/aggregate/cache ship as **raw-SQL migrations applied by the Job**.
  Seed `user/moderator/admin` + the action-bit catalog. Wire cache-invalidation events even if the bus relay is Phase 6.

---

## Phase 2 — Content (categories, threads, nested comments, tags, search)

**Goal.** The heart of the forum: categories, threads, **nested comments (materialized path, depth ≤ 5)**, tags,
FTS, keyset feeds, ownership + soft-delete.

**Depends on.** Phase 1 (authz, ownership, current user).

**Steps.**
1. **Schema `forum_content`:** `categories` (slug, visibility, owner, icon logical ref), `threads`
   (title, body markdown, `search_tsv`, owner, audit, soft-delete; keyset feed index
   `(category_id, is_pinned DESC, created_on_utc DESC, id DESC)`), `comments` (parent_id, **path**, depth,
   body, owner, soft-delete; index `(thread_id, path)`), `tags` + `thread_tags`.
2. **FTS:** `search_tsv` populated by a trigger (`title` weight A + `body` weight B) + GIN index.
3. **Use cases:** create/edit/delete **own** thread+comment (ownership), delete-**any** (moderator via ACL),
   change category, pin (moderator). Comment create computes `path = parent.path + own ULID`, enforces depth ≤ 5.
4. **Soft-delete:** flip flag; comment body → `"[deleted]"` keeping the subtree; global query filter hides deleted.
5. **Reads (views + keyset):** `thread_feed_v` (thread + author + category + counts), `comment_tree_v`
   (ordered by `(thread_id, path)`), `GET /threads?cursor=&limit=`, `GET /threads/{id}/comments`,
   `GET /search?q=` (FTS, ranked).
6. **Events (outbox):** `CategoryCreated`, `ThreadCreated/Updated/Deleted`, `CommentCreated/Deleted`; consume
   `UserBlocked` (hide/disable a blocked user's actions).
7. **Tests:** `Modules.Content.Tests` (path building, depth cap, soft-delete, keyset ordering), FTS integration test.

**Watch out.** Materialized-path ordering must be deterministic (ULID tiebreak). Enforce **depth ≤ 5** server-side.
Keyset cursor must encode the same sort columns as the index. Soft-deleted rows must never leak through views.
Authorize every write through the ACL + ownership (author vs `*.any`). Markdown is sanitized at render (frontend),
stored raw.

**Definition of Done.** Create thread → appears in feed (keyset); reply nests correctly to depth 5; author can
edit/delete own, moderator can delete any; FTS returns ranked hits; deleting a parent comment keeps children with
`"[deleted]"` body.

**START-OF-PHASE REMINDERS.**
- *Remember:* comments are **nested via materialized path, max depth 5, soft-delete keeps children**; lists are
  **keyset** (cursor matches the index columns + ULID tiebreak); search is **FTS (tsvector+GIN+trigger)**, not
  ILIKE; every write is authorized via ACL + **ownership** (author vs `*.any`); soft-delete query filter must hide deleted rows everywhere.

---

## Phase 3 — Files (direct-to-MinIO presigned uploads)

**Goal.** Image upload where bytes bypass the backend, plus the "file → which object" attachment link.

**Depends on.** Phase 1 (auth), Phase 2 (targets to attach to).

**Steps.**
1. **Schema `forum_files`:** `files` (bucket, object_key, content_type, size, dimensions, status
   pending/committed, uploaded_by, timestamps), `file_attachments` (file_id, **target_type, target_id**).
2. **Flow (ADR 0008):** `POST /files` → authorize, create `pending` row + content-addressed key, return
   **presigned PUT** URL (short TTL). Client `PUT`s **directly to MinIO**. `POST /files/{id}/commit` → HEAD the
   object, verify real content-type/size (never trust the declared values), decode dimensions, flip to
   `committed`, link `file_attachments`.
3. **Serving:** presigned GET (or CDN) — backend not in the byte path.
4. **Orphan sweep:** background task deletes `pending` rows past a grace window and committed blobs with no
   attachment.
5. **Events (outbox):** `FileCommitted`, `FileOrphaned`; consume `ThreadDeleted`/`CommentDeleted` → detach + sweep.
6. **Tests:** commit verification rejects type/size mismatch; attachment link integrity; sweep logic.

**Watch out.** **Never trust the declared content-type** — re-derive from the stored object on commit. Presigned
URL TTL must be short. MinIO needs **CORS** + a browser-reachable endpoint. The `(target_type, target_id)` link is
a **logical** reference (no cross-schema FK) kept consistent via events. Avatars/category icons attach via the
same mechanism (`target_type='avatar'|'category_icon'`).

**Definition of Done.** Browser uploads an image straight to MinIO via presigned URL; commit validates and
attaches it to a thread; an uncommitted upload is swept; deleting a thread detaches/cleans its files.

**START-OF-PHASE REMINDERS.**
- *Remember:* bytes **never go through the backend** (initiate → presigned PUT to MinIO → commit). On commit,
  **HEAD the object and re-verify content-type/size** (declared values are untrusted). The file's owning object is
  `(target_type, target_id)` — a logical link, not a DB FK; keep it consistent via `ThreadDeleted`/`CommentDeleted`
  consumers + an orphan sweep. MinIO CORS must allow the browser PUT.

---

## Phase 4 — Engagement (reactions + stats)

**Goal.** Likes/upvotes on threads and comments, and per-user stats.

**Depends on.** Phases 1–2.

**Steps.**
1. **Schema `forum_engagement`:** `reactions` (user_id, target_type, target_id, value, unique per user/target).
2. **Use cases:** toggle like/upvote (idempotent per user/target); `GET …/likes` (count + viewer state).
3. **Stats/counters:** `user_stats_v` view (karma, post/comment counts). **Decision:** unlike B, **denormalize hot
   counters** (thread like/comment counts) — maintained on `ReactionAdded/Removed` / `CommentCreated/Deleted`, or a
   refreshed materialized view — so the feed never does a full-table aggregate scan.
4. **Events (outbox):** `ReactionAdded/Removed`; consume `ThreadDeleted`/`CommentDeleted` (cascade reactions).
5. **Tests:** toggle semantics, count correctness, counter maintenance.

**Watch out.** Decide the counter strategy **here** and apply it consistently — this is a scalability talking
point vs B (which scans). Keep toggle idempotent (re-like is a no-op or unlike per the rule). Reactions reference
content by logical ULID (no cross-schema FK).

**Definition of Done.** Liking/unliking updates counts O(1) on read; feed shows correct counts without scanning
the reactions table; stats view returns correct karma.

**START-OF-PHASE REMINDERS.**
- *Remember:* pick a **counter strategy that avoids per-request full-table aggregates** (denormalized counters or
  refreshed matview) — this is exactly where B is weak, so A should be measurably better. Toggle is idempotent;
  cascade reactions on content delete via events.

---

## Phase 5 — Social *(OPTIONAL — only if B also builds it)*

**Goal.** Friends list + simple **text** 1:1 DMs.

**Depends on.** Phases 1, 6 (DMs are nicer over WS), 7.

**Steps.** `forum_social`: `friendships` (pending/accepted), `direct_messages` (text only). Use cases: send/accept/
remove friend; send/list DM (gated on friendship). Events: `FriendRequestSent/Accepted`, `DirectMessageSent`.

**Watch out.** Gate DMs on accepted friendship. **No voice notes / presence / read-receipts** (OUT of scope).
Only build if Hubert builds the equivalent on B, so it stays comparable.

**Definition of Done.** Friend request/accept works; DMs deliver only between friends.

**START-OF-PHASE REMINDERS.**
- *Remember:* this phase is **optional and text-only**; skip it unless B implements the same. Gate on friendship; no voice/presence.

---

## Phase 6 — Messaging backbone (RabbitMQ + outbox relay + consumers)

**Goal.** Turn the per-module outbox tables into real cross-module async reactions over RabbitMQ.

**Depends on.** Phases 1–4 (modules emitting events to their outbox).

**Steps.**
1. **Outbox relay** (background service): poll each module's `outbox_messages WHERE processed_on_utc IS NULL`,
   publish to a **topic exchange per source module** (routing key = event name), mark processed; retry/backoff.
2. **Consumers:** each module binds its queues to the exchanges it cares about; handlers are **idempotent** (inbox
   dedupe by `EventId`). Wire the real cross-module reactions (Files detach on ThreadDeleted, Engagement cascade,
   Authz cache recompute).
3. **Correlation:** propagate `CorrelationId` from request → outbox → consumer logs/traces.
4. **Readiness:** `/health/ready` gates on RabbitMQ connectivity.
5. **Tests:** outbox written in same tx as state change; consumer idempotency (duplicate delivery is a no-op);
   end-to-end event flow integration test.

**Watch out.** The event row must be written **in the same DB transaction** as the state change (no "saved but not
published" gap). Consumers must tolerate **at-least-once** + reordering. Don't let a poison message block the
queue (dead-letter/backoff). Contracts live in `*.Contracts` as versioned immutable records.

**Definition of Done.** Creating/deleting content triggers the right cross-module consumers asynchronously;
duplicate deliveries are idempotent; readiness flips false when RabbitMQ is down.

**START-OF-PHASE REMINDERS.**
- *Remember:* outbox write is **transactional with the state change**; consumers are **idempotent** (dedupe by
  EventId) and tolerate reordering; one topic exchange per source module; propagate correlation-id; readiness gates RabbitMQ.

---

## Phase 7 — Real-time WebSocket (fetch-then-patch)

**Goal.** Push compact change-notifications to the SPA so it patches in place; no polling.

**Depends on.** Phase 6 (events on the bus).

**Steps.**
1. **WS hub** in `Forum.Api`: authenticate the socket at connect; per-view subscription (category/thread/user).
2. **Fan-out consumer:** subscribe to relevant integration events; relay `{ type, entity, id, parentId?,
   categoryId?, version }` to subscribed clients — **re-check visibility** before each push (never push
   private-category changes to non-members).
3. **Scale-out:** every replica receives every event from the bus and pushes to its own sockets; reconnect →
   client resync (re-fetch current view).
4. **Tests:** notification routing/scoping; private content not pushed to non-members; reconnect resync.

**Watch out.** Push **notifications, not full entities** (avoid leaking fields / oversend). Authorization on every
push. Client must resync on reconnect (missed events self-heal). No sticky sessions needed for correctness.

**Definition of Done.** A new thread/comment/like appears live in another client without reload; a private-category
post is not pushed to a non-member; reconnect re-syncs.

**START-OF-PHASE REMINDERS.**
- *Remember:* WS carries a **compact change-notification** (id + routing + version), not the full entity; **re-check
  visibility before every push**; the client **fetches once then patches** and **resyncs on reconnect**; fan-out is just another bus consumer.

---

## Phase 8 — Frontend (React SPA)

**Goal.** The decoupled React client consuming the REST API + WebSocket.

**Depends on.** Phases 1–4, 7 (a thin slice can start after Phase 2).

**Steps.**
1. **API client** (typed), **AuthContext** (access token in JS memory, refresh in httpOnly cookie, silent refresh
   on 401 via single-flight interceptor), **React Query** (keyset infinite scroll, per-view cache keys).
2. **Pages:** feed, thread (comment tree), category, profile + stats, login/register, admin (users/roles/block).
3. **Real-time:** one WebSocket; on a change-notification `invalidateQueries`/`setQueryData` for the affected key.
4. **Upload widget:** presigned flow (initiate → PUT to MinIO → commit) with progress.
5. **Tests:** component + a Playwright e2e for the core journeys (login, post, comment, search).

**Watch out.** Access token never in localStorage (XSS) — memory + httpOnly refresh cookie. 401 → single-flight
refresh → retry. Keyset infinite scroll must pass the cursor, not a page number. WS reconnect/resync handled.

**Definition of Done.** End-to-end: register → login → browse feed (infinite scroll) → open thread → comment →
see another client update live → upload an image. Lighthouse run captured.

**START-OF-PHASE REMINDERS.**
- *Remember:* access token in **memory only**, refresh in **httpOnly cookie**, single-flight 401 refresh;
  **keyset** infinite scroll (cursor, not page); WS patches the React Query cache and resyncs on reconnect; upload uses the **presigned** flow.

---

## Phase 9 — Seed + benchmark harness + observability finalization

**Goal.** Make A measurable and comparable to B under the shared harness.

**Depends on.** Phases 1–8 (MUST scope complete).

**Steps.**
1. **Deterministic seed** identical in shape to B's (same users/categories/threads/comments volume).
2. **k6 scenarios** for the **shared measured scenarios** (feed, open thread, create post/comment, login, search);
   profiles smoke/load/stress.
3. **Domain metrics** (auth attempts by outcome, threads/comments created, reactions, outbox lag, WS connections)
   + **Grafana dashboards**; Lighthouse run script; correlation-id on every log/trace.
4. **Sampling:** CPU/RAM per pod, HPA pod count, DB pool — exported to Prometheus and dashboards.

**Watch out.** Fair comparison = **same seed, same resource limits, isolated runs, warm-up, multiple runs +
variance**. Don't set `GOMX_SKIP_VITALS`-style lab-only flags on the measured runs. Keep scenarios logically
identical to B's.

**Definition of Done.** One command runs each k6 profile against a deployed A; dashboards show throughput/latency/
resources/HPA; numbers are reproducible across runs.

**START-OF-PHASE REMINDERS.**
- *Remember:* the benchmark must be **fair** — identical seed + resource limits + isolation + warm-up + repeats vs
  B; measure perf-first metrics (throughput, p95/p99, max concurrent users at SLO, CPU/RAM, HPA); scenarios mirror B.

---

## Phase 10 — k8s deploy, hardening & comparative run

**Goal.** Run the full stack on minikube, hardened, and collect the thesis comparison data.

**Depends on.** Phase 9.

**Steps.**
1. Deploy Postgres, RabbitMQ, MinIO, backend, frontend, ingress, **migration Job**, HPA, PDB, NetworkPolicies,
   monitoring (kube-prometheus-stack + Loki + Tempo + Grafana).
2. **securityContext:** non-root, read-only rootfs, drop ALL caps, seccomp RuntimeDefault. Pool sizing so
   `replicas × pool ≤ max_connections`.
3. Run the comparative benchmark vs B (same harness, isolation) and capture results into `thesis/`.

**Watch out.** Migrations run via the **Job before rollout**, not at pod startup (HPA race). Tune Npgsql pool vs
`max_connections`. Keep secrets in k8s Secret, not git. Run A and B **one at a time**.

**Definition of Done.** `scripts/deploy.sh` brings up a healthy stack; migration Job completes before pods serve;
comparative benchmark data collected and archived.

**START-OF-PHASE REMINDERS.**
- *Remember:* migrations via the **k8s Job before rollout**; harden containers (non-root, ro-rootfs, drop caps);
  `replicas × pool ≤ max_connections`; benchmark A and B **in isolation** with identical conditions; archive results in `thesis/`.

---

## Change log
- **2026-06-24** — initial plan (Phases 0–10), aligned with REQUIREMENTS-AND-ASSUMPTIONS.md, the ADRs, and the golden-mean scope (forum-spec v0.2).
