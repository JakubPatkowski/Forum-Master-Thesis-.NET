# Architecture A — Requirements & Assumptions (master spec)

> **Status:** authoritative · **Updated:** 2026-06-24 · **Language:** English (repo convention; thesis prose stays Polish)
> **Scope:** the binding design assumptions for `forum-dotnet` (Architecture A). Read this first, then the
> per-area docs it links. Companion: [`DOMAIN-MODEL-AND-DATABASE.md`](./DOMAIN-MODEL-AND-DATABASE.md),
> [`permissions-acl-design.md`](../db/permissions-acl-design.md), ADRs `0001`–`0010`.
>
> **Relation to the thesis:** A is the React SPA + .NET 10 side, benchmarked against B (`../gomx`, Go SSR).
> The agreed common scope ("golden mean") lives in `../../../docs/specs/forum-spec.md` and
> `../../../docs/architecture/PROPOZYCJA-UJEDNOLICENIA-A-B.md`. **B adapts to this contract; A is not modeled on B.**

---

## 0. The ten non-negotiable assumptions

These are the project's load-bearing decisions. Everything else serves them.

1. **Professional relational schema**, modeled on `ProjektForumWedkarskie` (roles/permissions/refresh-tokens/
   FTS/materialized-path comments) but extended to the thesis requirements: **RBAC + bitmask ACL**, global roles
   `user/moderator/admin` **and** per-context roles (category/group membership), **ownership**, **soft-delete**,
   and full audit on every aggregate root. See [§3](#3-cross-cutting-data-rules) + the DB doc.
2. **ULID everywhere** — every identifier is a ULID (26-char Crockford base32). No integers, no GUIDv4 in URLs.
   See [ADR 0006](./adr/0006-ulid-everywhere.md).
3. **Argon2id** password hashing. See [ADR 0007](./adr/0007-argon2id-password-hashing.md).
4. **Direct-to-MinIO uploads** via presigned URLs — file bytes never transit the backend.
   See [ADR 0008](./adr/0008-direct-to-minio-presigned-uploads.md).
5. **Module-first modular monolith** with a **RabbitMQ** message bus; modules communicate across boundaries
   **only** via `*.Contracts` + **integration events** on the bus (transactional outbox).
   See [ADR 0002](./adr/0002-hexagonal-modular-monolith.md) + [ADR 0009](./adr/0009-rabbitmq-inter-module-events.md).
6. **Real-time via WebSocket**: when an aggregate changes, a change notification is pushed to the SPA; the SPA
   loads everything it needs up front, then **patches in place** on each WS event (no polling).
   See [ADR 0010](./adr/0010-websocket-realtime-aggregate-changes.md).
7. **Observability**: Serilog → Loki (structured logs + correlation id), OpenTelemetry traces → Tempo,
   Prometheus metrics → Grafana dashboards; `/health/live` + `/health/ready`.
8. **All functional scope comes from the agreed "golden mean"** ([§1](#1-functional-scope-golden-mean)).
9. **Reads via SQL views + keyset pagination; writes via aggregates + UnitOfWork.** No N+1. Result pattern, no
   exceptions for expected failures. Validation order **404 → 403 → 422**.
10. **Migrations run as a Kubernetes Job**, never at pod startup (avoids the HPA migration race).
    See [ADR 0005](./adr/0005-migrations-as-k8s-job.md).

---

## 1. Functional scope (golden mean)

Agreed common scope with B (text-first discussion forum). Implemented **identically** on both sides so the
comparison measures architecture, not feature count.

### MUST (in scope, measured)

- **Accounts & auth:** register, login, logout, refresh; email + password (Argon2id); account status
  (active/blocked).
- **Roles & permissions:** global `user/moderator/admin`; per-context roles via category/group membership;
  RBAC + bitmask ACL ([permissions-acl-design.md](../db/permissions-acl-design.md)).
- **Categories** (containers for threads): public/private, owner, optional icon.
- **Threads (posts):** create, read (feed with keyset paging), read one, **edit/delete own** (author ownership),
  delete-any (moderator), change category, soft-delete.
- **Comments:** **nested (materialized path, max depth 5)**, create, read tree, edit/delete own, delete-any (mod),
  soft-delete (content → "[deleted]", children remain).
- **Tags:** M:N with threads.
- **Reactions:** like/upvote on threads and comments (per-user, toggleable).
- **Files/attachments:** image upload (direct-to-MinIO), attach to thread/comment/category-icon/avatar.
- **Search:** full-text (Postgres `tsvector` + GIN) over thread title/body; user & category lookup.
- **Profile + stats:** a user's threads/comments and aggregate karma/counts (via SQL view).
- **Real-time:** WebSocket push of new/changed threads & comments to open clients.
- **Ops surface:** `/health/live`, `/health/ready`, `/metrics`, OpenAPI.

### OPTIONAL (in scope only if both sides implement it; measured separately)

- Friends list (request/accept/remove).
- Basic 1:1 **text** direct messages.

### OUT (explicitly excluded from the comparison)

- Voice notes, presence/online, read-receipts, friend-add policies, themes, full i18n, desktop/mobile shells.
  (B may keep these; A does not implement them; neither is measured.)

---

## 2. Module map & ownership

Module-first: each module is one project `Forum.Modules.X` with `Domain/ Application/ Infrastructure/
Presentation/ Contracts/` and a `XModule : IModule` installer. Everything `internal` except `Contracts/`.
**Each module owns its own Postgres schema, DbContext, and migration chain.** Cross-module access is via
`*.Contracts` (sync queries exposed deliberately) and **integration events** (async) — never a direct project
reference into another module's internals, and **never a cross-schema database FK**.

| Module | Schema | Owns (aggregates / tables) | Publishes events | Consumes events |
|---|---|---|---|---|
| **Identity** | `forum_identity` + `forum_authz` | User, RefreshToken; roles, permissions, ACL entries, effective-perm cache | `UserRegistered`, `UserBlocked`, `RoleAssigned`, `AclEntryChanged` | — |
| **Content** | `forum_content` | Category, Thread, Comment, Tag | `ThreadCreated/Updated/Deleted`, `CommentCreated/Deleted`, `CategoryCreated` | `UserBlocked` (hide content), `FileCommitted` (attach) |
| **Files** | `forum_files` | File, FileAttachment | `FileCommitted`, `FileOrphaned` | `ThreadDeleted`/`CommentDeleted` (detach/sweep), `UserRegistered` |
| **Engagement** | `forum_engagement` | Reaction; UserStats (view) | `ReactionAdded/Removed` | `ThreadDeleted`/`CommentDeleted` (cascade reactions) |
| **Social** *(optional)* | `forum_social` | Friendship, DirectMessage | `FriendRequestSent/Accepted`, `DirectMessageSent` | `UserBlocked` |
| **Notifications/Audit** *(later)* | `forum_audit` | audit log | — | (all, for audit trail) |

> **Why no cross-schema FK:** a hard FK would couple two modules' schemas and break independent migration +
> the architecture tests. Cross-module references are **logical** (store the foreign ULID + type) and integrity
> is maintained by **integration events** (e.g. Files reacts to `ThreadDeleted` to detach/sweep blobs).

---

## 3. Cross-cutting data rules

Applied uniformly (enforced by `Forum.SharedKernel` base types + an EF audit interceptor).

- **Identifiers:** every PK is a **ULID** (`Ulid` value type in C#, stored per the DB doc). Public API exposes the
  ULID directly (sortable, non-enumerable). No sequential integers anywhere.
- **Audit columns on every aggregate root:** `created_on_utc`, `created_by`, `last_modified_on_utc`,
  `last_modified_by`. Stamped by the **AuditInterceptor** in `SaveChanges` from `ICurrentUser` — handlers never
  set them by hand. `*_by` are user ULIDs (logical reference to Identity).
- **Soft-delete:** aggregates that can be removed carry `is_deleted` (+ `deleted_on_utc`, `deleted_by`). A global
  EF query filter hides soft-deleted rows; "delete" flips the flag. Comments additionally blank their body to
  `"[deleted]"` while keeping children (materialized-path subtree intact).
- **Ownership:** owned aggregates carry `owner_id` (the creator, a user ULID). Authorization distinguishes
  **owner** (`*.update`/`*.delete` on own) from **any** (`*.update.any`/`*.delete.any`, moderator/admin),
  resolved through the ACL ([permissions-acl-design.md](../db/permissions-acl-design.md)).
- **Files reference their target:** a file is attached to exactly one object via
  `(target_type, target_id)` (e.g. `thread`/`comment`/`category_icon`/`avatar`/`dm`). This is the "FK to the
  object it belongs to" requirement — a logical reference (not a cross-module DB FK), validated on commit and
  kept consistent via events.
- **Timestamps** are `timestamptz` in UTC. **Naming** is snake_case in the DB, PascalCase in C# (EF naming
  convention). **Money/counters** are never floats.

---

## 4. API & transport conventions

- **REST `/api/v1`, JSON.** Resource URLs use the ULID public id. One endpoint = one file (`IEndpoint`) in the
  owning module's `Presentation/`.
- **Result → HTTP** at the edge: `Result`/`Result<T>` mapped by a single problem-details mapper to the envelope
  `{ code, description, type }`. Validation order **404 (not found) → 403 (forbidden) → 422 (validation)**.
- **Pagination:** **keyset** (`?cursor=&limit=`) for all lists (threads, comments, search). Never OFFSET.
- **AuthN:** **JWT access (15 min) in JS memory + refresh (14 d) in httpOnly cookie**, with **rotation +
  reuse-detection** (refresh token family). Argon2id for passwords. See [ADR 0007](./adr/0007-argon2id-password-hashing.md).
- **AuthZ:** `RequirePermission("thread.create")` filters at the endpoint; ownership checks in the handler;
  effective permission resolved in SQL.
- **Security headers, explicit CORS allow-list, per-IP rate limiting on auth, request body size limits** — all in
  `Forum.Api` (Bootstrap).

---

## 5. Inter-module messaging (RabbitMQ) — see ADR 0009

- **In-process domain events** stay inside a module (aggregate `Raise(...)` → dispatched in
  `SaveChangesAndDispatchEventsAsync` → in-module handlers).
- **Integration events** (cross-module) go through the **transactional outbox**: written in the same DB
  transaction as the state change, then a relay publishes them to **RabbitMQ** (topic exchange per source
  module). Consumers are **idempotent** (dedupe by event id) and run in their own module.
- **Contract:** integration events live in `*.Contracts` as immutable records with a stable `EventId` (ULID),
  `OccurredOnUtc`, and a versioned name. The event catalog is the table in [§2](#2-module-map--ownership).
- **Why outbox + bus rather than direct calls:** keeps modules decoupled, gives at-least-once delivery,
  survives consumer downtime, and makes the WebSocket fan-out (below) a normal consumer.

---

## 6. Real-time (WebSocket) — see ADR 0010

**Model:** the SPA fetches the full view it needs on navigation (REST), opens one WebSocket, and **patches its
local cache** on each change event. No polling, no re-fetch of the whole list.

- **Source of truth:** integration events already on RabbitMQ (`ThreadCreated`, `CommentCreated`,
  `ReactionAdded`, …). A **WebSocket fan-out consumer** in `Forum.Api` subscribes and relays a small JSON
  **change notification** to interested clients.
- **Change notification shape** (not the full entity — the client decides whether to patch or re-fetch):
  `{ type, entity: "thread"|"comment"|"reaction", id, parentId?, categoryId?, version }`.
- **Targeting:** broadcasts are scoped (e.g. by category / thread / user) so a client only receives what its
  current view cares about; private-category content is never pushed to non-members.
- **Scale-out:** the WS hub is backed by the bus, so any replica can serve any socket; with N pods every pod
  receives every relevant event from RabbitMQ and pushes to its own connected clients. (Frontend reconnect +
  resync on connect handles missed events.)
- **Frontend pattern (React Query):** on a change notification, `invalidateQueries`/`setQueryData` for the
  affected key — the SPA re-renders only the touched part.

---

## 7. Observability (see ADR-less area, `infrastructure/monitoring`)

- **Logs:** Serilog structured JSON → Loki. **Correlation-Id** middleware assigns/propagates a request id on every
  request and onto every log line and outgoing bus message.
- **Traces:** OpenTelemetry (ASP.NET Core, EF Core, Npgsql, RabbitMQ, HttpClient instrumentation) → Tempo.
- **Metrics:** Prometheus at `/metrics` — RED metrics by route, plus domain counters (auth attempts by outcome,
  threads/comments created, reactions, outbox lag, WS connections) → Grafana dashboards.
- **Health:** `/health/live` (process up) + `/health/ready` (DB + RabbitMQ reachable; gates k8s readiness).

---

## 8. Non-functional targets (perf-first, for the benchmark)

Primary metrics (matching the revised thesis emphasis — throughput, latency, concurrent users, security; raw
artifact size is secondary): sustained req/s, p50/p95/p99 latency, **max concurrent users at SLO**
(e.g. p95 < 300 ms, error < 1%), CPU-seconds / 1000 requests, RAM working set, HPA scaling behavior, DB pool
saturation. Measured with the shared harness (k6 + Prometheus + Lighthouse), same seed, same resource limits,
isolated runs. See `../../../docs/architecture/PROPOZYCJA-UJEDNOLICENIA-A-B.md` §12.

---

## 9. Deployment & data

- **PostgreSQL 17**, one database `forum_net`, **one schema per module**, one migration Job.
- **Pooled DbContext** + tuned Npgsql pool so `replicas × pool ≤ max_connections`.
- **MinIO** for blobs (direct upload), **RabbitMQ** for the bus, all on k8s (minikube) with HPA, PDB,
  NetworkPolicies, securityContext (non-root, read-only rootfs, drop caps). Secrets via k8s Secret /
  `dotnet user-secrets` in dev — never in `appsettings*.json` or git.

---

## 10. Document index

| Doc | What |
|---|---|
| `REQUIREMENTS-AND-ASSUMPTIONS.md` (this) | Master assumptions + functional scope |
| `DOMAIN-MODEL-AND-DATABASE.md` | Full schema (DDL sketch), per-module tables, conventions |
| `db/permissions-acl-design.md` | RBAC + bitmask ACL resolved in SQL |
| `adr/0002` | Hexagonal modular monolith |
| `adr/0003` | CQRS without MediatR |
| `adr/0004` | SQL bitmask ACL |
| `adr/0005` | Migrations as a k8s Job |
| `adr/0006` | ULID everywhere |
| `adr/0007` | Argon2id password hashing |
| `adr/0008` | Direct-to-MinIO presigned uploads |
| `adr/0009` | RabbitMQ inter-module integration events (outbox) |
| `adr/0010` | WebSocket real-time on aggregate change |
