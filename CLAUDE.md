# forum-dotnet — Working Memory (v0.2, scaffold + locked assumptions)

## What this is
**Architecture A** of the master's thesis: decoupled **React SPA + .NET 10** forum. Self-contained git repo
(backend + frontend + db + k8s + infra + scripts + CI). Benchmarked against **Architecture B** (colleague's
Go SSR monolith, `../gomx`). The colleague adapts to THIS spec — do not mirror gomx. Goal: professional,
deliberately slightly over-engineered, to showcase best practices. Authors: Jakub Patkowski (95818) &
Hubert Ożarowski (97692); supervisor Dr inż. Kamil Żyła.

## Authoritative design docs (read these; they win over any stale phrasing below)
- **`docs/architecture/REQUIREMENTS-AND-ASSUMPTIONS.md`** — master spec: the 10 locked assumptions + functional scope.
- **`docs/architecture/DOMAIN-MODEL-AND-DATABASE.md`** — full schema (ULID, audit, soft-delete, ownership, files→target, FTS, keyset).
- **`docs/architecture/IMPLEMENTATION-PLAN.md`** — phased build plan (Phases 0–10) with per-phase steps, gotchas, Definition of Done, and START-OF-PHASE REMINDERS. Re-read the relevant phase block before coding.
- **`docs/architecture/FOUNDATION-AND-SHARED-ABSTRACTIONS.md`** — Phase 0 result: every shared abstraction (SharedKernel / Common / Infrastructure / Api wiring) + the foundation assumptions every module inherits.
- **`docs/db/permissions-acl-design.md`** — RBAC + bitmask ACL resolved in SQL.
- **ADRs:** 0002 modular monolith · 0003 CQRS no MediatR · 0004 SQL ACL · 0005 migrations as k8s Job · **0006 ULID everywhere** · **0007 Argon2id** · **0008 direct-to-MinIO presigned uploads** · **0009 RabbitMQ integration events (outbox)** · **0010 WebSocket real-time (fetch-then-patch)**.
- Shared scope contract with B: `../../docs/specs/forum-spec.md` + `../../docs/architecture/PROPOZYCJA-UJEDNOLICENIA-A-B.md`.

## 10 locked assumptions (full detail in REQUIREMENTS-AND-ASSUMPTIONS.md)
1. Professional relational schema (modeled on forum-wędkarskie) + RBAC **and** bitmask ACL; global roles `user/moderator/admin` + per-context roles (category/group); ownership; soft-delete; full audit on every aggregate root.
2. **ULID everywhere** (no ints/GUIDv4 in URLs). 3. **Argon2id** hashing. 4. **Direct-to-MinIO** presigned uploads (bytes bypass backend).
5. **Module-first** monolith + **RabbitMQ** integration events via transactional **outbox** (no cross-module/-schema coupling). 6. **WebSocket** change-notifications on aggregate change; SPA fetches then patches in place.
7. Observability: Serilog→Loki + OTel→Tempo + Prometheus→Grafana; `/health/{live,ready}`. 8. Functional scope = the golden mean.
9. Reads via SQL views + **keyset**; writes via aggregates + UnitOfWork; Result pattern; **404→403→422**. 10. Migrations as a **k8s Job**, not at startup.

**Files→object rule:** every file links to exactly one target via `(target_type, target_id)` — the "FK to the object it's attached to" (logical ref, kept consistent by events; no cross-schema DB FK). Aggregate roots all carry `created_on_utc/created_by/last_modified_on_utc/last_modified_by`; owned ones add `owner_id`; removable ones add `is_deleted/deleted_on_utc/deleted_by`.

## Golden rules
- **English** for all code, identifiers, comments. Polish only for thesis prose/docs.
- **Module-first**: solution organized by business module, not layer. Each module = 1 project with `Domain/ Application/ Infrastructure/ Presentation/ Contracts/` folders. Everything `internal` except `Contracts/`; modules interact only via Contracts + integration events. `Forum.ArchitectureTests` enforces module boundaries AND in-module layer purity (Domain free of Infrastructure/framework).
- Logic lives in `Core.Application` use-case handlers; endpoints/consumers are thin. Handlers return `Result`/`Result<T>` — **no exceptions for expected failures**.
- Reads go through **SQL views + view repositories**; writes through **aggregates + repository + UnitOfWork**. Avoid N+1; prefer keyset pagination.
- Central Package Management only — **never** put `Version=` in a `.csproj`; add it to `backend/Directory.Packages.props`.
- Secrets: `dotnet user-secrets` (dev) / k8s Secret (cluster). Never in `appsettings*.json` or git.
- **Mount-lag caveat:** after writing a file in this sandbox, `cat` may show a stale/truncated copy — trust the Read tool / re-list, don't "fix" phantom truncation.

## Layout (13 projects, module-first)
`src/` has exactly three folders:
- **Bootstrap/** — `Forum.Api` (the only executable: Program.cs, auth, middleware, OTel, health, OpenAPI; discovers `IModule`s and wires them).
- **Shared/** — `Forum.SharedKernel` (Entity, AggregateRoot, Result, Error), `Forum.Common` (CQRS abstractions, `IModule`/`IEndpoint`, in-process event bus, paging, correlation), `Forum.Infrastructure` (shared adapter base: EF base, outbox, RabbitMQ, MinIO, startup tasks).
- **Modules/** — `Identity · Content · Files · Engagement` (each = 1 project `Forum.Modules.X` with `Domain/ Application/ Infrastructure/ Presentation/ Contracts/` + `XModule.cs`). `Notifications, Audit` added later. A `Modules/Directory.Build.props` gives every module the same baseline (frameworks + EF + validation + shared refs) so module csprojs stay tiny.

Tests: `ArchitectureTests · Modules.Identity.Tests · Modules.Content.Tests · IntegrationTests · TestUtilities`. Each module owns its **schema + DbContext + migrations**; cross-module refs go through `*.Contracts` only.

## Patterns (from a prior production .NET service, improved)
- **CQRS without MediatR**: `ICommand/IQuery` + handlers (`Forum.Common`), registered via **Scrutor** scan in `Core.Application`.
- **Result pattern**: `Result`, `Result<T>`, `Error`, `ErrorType` (`Forum.SharedKernel/Results`). One error→HTTP mapper at the REST edge → envelope `{ code, description, type }` (404→403→422 order).
- **Domain events**: aggregate `Raise(...)` → collected in DbContext `SaveChangesAndDispatchEventsAsync` → handlers in `Core.Application`. Cross-service events go through the **outbox** (`Adapters.Out.Messaging`), not in-process.
- **Audit interceptor** stamps created/modified by/on. **Ulid** keys. EF: `NoTracking` reads, `SplitQuery`, snake_case naming.
- **Minimal API `IEndpoint`**: 1 file = 1 endpoint, in a module's `Presentation/`, mapped by that module's `XModule.MapEndpoints`.
- **Startup tasks** (`Forum.Infrastructure/Startup`): ordered migration → views → seed. In k8s migrations run as a **Job**, not at startup (HPA race) — `Forum.Api` supports a `migrate` arg.
- **Module installer** (`XModule : IModule` in each module): registers DI + maps endpoints + owns the module's schema/migrations. `Forum.Api` holds the explicit module list.

## Security (from forum-wędkarskie, in .NET)
JWT access 15 m + refresh 14 d with **rotation + reuse-detection**; refresh in httpOnly cookie, access in JS memory.
**Argon2id** hashing. **RBAC + bitmask ACL resolved in SQL** — see `docs/db/permissions-acl-design.md` and ADR 0004
(int_or_agg aggregate, `effective_mask` resolver, `effective_perm_cache`, partial + BRIN indexes). Security headers,
rate limiting, explicit CORS allow-list, upload size limit — all in `Host`.

## Database
PostgreSQL 17, one DB (`forum_net`), **one schema + DbContext + migration chain PER MODULE**
(`forum_identity`, `forum_authz`, `forum_content`, `forum_files`, `forum_engagement`, `forum_social`). **No FK crosses a schema**
(cross-module links are logical ULIDs kept consistent via integration events). Views for read models; `tsvector` FTS;
**keyset** pagination; pooled DbContext + tuned Npgsql pool (replicas × pool ≤ max_connections).
ACL functions/aggregate/`int_or_agg`/`effective_mask()` ship as raw-SQL EF migrations in the Identity module's `Infrastructure/Acl/`.
Full schema: `docs/architecture/DOMAIN-MODEL-AND-DATABASE.md`.

## Observability
Serilog → Loki; OpenTelemetry traces → Tempo; metrics → Prometheus (`/metrics`); Grafana dashboards.
Correlation-Id middleware; `/health/live` + `/health/ready` (readiness gates DB/RabbitMQ).

## Commands
```bash
cd backend
dotnet restore Forum.slnx
dotnet build   Forum.slnx
dotnet test    Forum.slnx                  # unit + architecture + integration (Testcontainers → Docker required)
dotnet format  Forum.slnx                  # style (CI verifies --verify-no-changes)
dotnet run --project src/Bootstrap/Forum.Api
# per-module migrations (each module owns its DbContext):
dotnet ef migrations add <Name> -p src/Modules/Identity/Forum.Modules.Identity -s src/Bootstrap/Forum.Api -c IdentityDbContext
```
Cluster: `../scripts/setup-minikube.sh` → `../scripts/deploy.sh` (builds image into minikube, runs migration Job, applies manifests). Load: `../scripts/run-load-test.sh smoke`.

## Build order (next work) — see `docs/architecture/IMPLEMENTATION-PLAN.md` for full per-phase detail
Phase 0 Foundation/build-green → 1 Identity+Authz → 2 Content → 3 Files → 4 Engagement →
5 Social (OPTIONAL) → 6 Messaging backbone (RabbitMQ+outbox) → 7 WebSocket → 8 Frontend →
9 Seed+benchmark+observability → 10 k8s deploy+hardening+comparative run.
Each phase block has Goal · Depends on · Steps · Watch out · Definition of Done · Events · START-OF-PHASE REMINDERS.
When the user says "finished phase X, start phase Y", re-read that phase block + the referenced docs first.

## Current state
**Phase 0 complete** (branch `feat/phase-0-foundation`) — `dotnet build`/`test`/`format` green on .NET 10, `/health/live` smoke test passes, ArchitectureTests green.
- **SharedKernel**: `Result`/`Error`, `Entity`, `AggregateRoot` (domain events + audit fields), `IAuditableEntity`/`IHasDomainEvents`/`ISoftDeletable`/`IOwned`.
- **Forum.Common**: CQRS markers, `IModule`/`IEndpoint`, `IEventBus`+`IIntegrationEventHandler`, paging, `ICorrelationContext`, `ICurrentActor`.
- **Forum.Infrastructure**: `ForumDbContext` base (no-tracking reads, soft-delete query filter, `SaveChangesAndDispatchEventsAsync`), `AuditInterceptor`, `DomainEventDispatcher`, `InMemoryEventBus`, `OutboxMessage`(+config), lazy `RabbitMqConnection`, `MinioObjectStorage`, `AddForumInfrastructure`, `ModuleDbContextRegistration` (snake_case + interceptor), `RunMigrationsAsync`.
- **Forum.Api**: correlation-id middleware + `CorrelationContext`, ProblemDetails exception handler, CORS allow-list, rate limiter, JWT bearer + authz skeleton, `migrate` arg hook wired in `Program.cs`.
- **Tests**: `ArchitectureTests` (boundary + Domain purity), `PostgresFixture` (Testcontainers), `HealthCheckTests` smoke test.
- Build hygiene applied: CPM fixed (no `--` in XML comments), NuGet-audit + a few CA/style rules tuned in `.editorconfig`/`Directory.Build.props`.

**Phase 1 — Identity + Authz: code-complete (2026-06-27).** Module `Forum.Modules.Identity` (Domain/Application/Infrastructure/Presentation/Contracts), registered in `Program.cs`.
- **`forum_identity`** (EF migration `InitialIdentity`): `users` (citext email, `username_lc` unique, **Argon2id** via Isopoh, status, audit) + `refresh_tokens` (family, SHA-256 hash, rotation chain). **`forum_authz`** (raw-SQL migration `AddAuthzSchema` in `Infrastructure/Acl/AuthzSchema.cs`): actions/roles/user_roles/acl_entries/effective_perm_cache, `int_or_agg`, `effective_mask()`/`has_permission()`/`recompute_user_perms()`, hot-path/partial/BRIN indexes, role+action seed.
- **Use cases** (Result): register, login (non-revealing + dummy verify), refresh (rotation + **family reuse-detection**), logout, logout-all, admin list/roles/ACL/status. Endpoints `/api/identity/*` + `/admin/users/*`, httpOnly refresh cookie, tighter auth rate-limit.
- **Shared authz surface** added to `Forum.Common`: `ICurrentUser`, `IPermissionService`, `RequirePermission` filter, `JwtOptions` (dev signing-key fallback), `RateLimitPolicies`, `ApiResults` (404→403→422). `CurrentUser` backs `ICurrentActor`; resolves `IPermissionService` lazily to avoid a DbContext↔AuditInterceptor DI cycle.
- **Verified green:** `dotnet build`, `dotnet format --verify-no-changes`, 15 Identity unit tests, ArchitectureTests.
- **Phase 1 DoD closed (2026-07-01):** Docker available again — `AclSqlTests` + `IdentityFlowTests` green under Testcontainers. `dotnet ef` 10.0.2 via tool manifest (`dotnet tool restore`).

**Phase 2 — Content: code-complete + fully verified (2026-07-01, branch `5-feat-phase-2---content`).** Module `Forum.Modules.Content` filled in (was a scaffold), already registered in `Program.cs`.
- **`forum_content`** — migration `InitialContent` (EF): `categories` (slug unique, visibility as text, owner, soft-delete, audit), `threads` (markdown raw, pinned, keyset index `ix_threads_feed (category_id, is_pinned DESC, created_on_utc DESC, id DESC) WHERE is_deleted=false`), `comments` (materialized `path` ≤161 chars, depth ≤ 5, `ix_comments_thread_path`), `tags`+`thread_tags`, `outbox_messages`. Migration `AddFtsAndViews` (raw SQL in `Infrastructure/Fts/ContentFtsAndViews.cs`): `search_tsv` **outside the EF model** + trigger (title A / body B, 'simple') + GIN, views `thread_feed_v` / `comment_tree_v` (keeps deleted rows) / `thread_detail_v` (tags as text[]); views JOIN `forum_identity.users` — view-level read join, Identity migrates first (module registration order). Migrations folder has its own `.editorconfig` (`generated_code=true`), same as Identity.
- **Domain:** `Category`/`Thread`/`Comment` aggregates (IOwned+ISoftDeletable; a **global using-alias** resolves the `Thread` vs `System.Threading.Thread` clash), `Comment.CreateReply` enforces `MaxDepth=5`, delete → body `"[deleted]"` keeping children; `Tag`+`ThreadTag`.
- **Use cases** (Result, 404→403→422 order in handlers): category CRUD, thread create (tags get-or-create)/update/delete/pin/change-category, comment create/update/delete, feed/search/tree/detail queries. **Permission mapping onto the Phase 1 action catalog** (prompt names like `thread.create`/`*.any` don't exist): create=`create`, comment=`comment`, moderator powers=`moderate` resolved **at category scope inside handlers** (covers global roles AND per-category `acl_entries`); ownership via `ICurrentUser.IsOwner`. Integration events in `Contracts/` (Category/Thread/Comment Created/Updated/Deleted) written via module-local `IOutboxWriter` (same as Identity); `UserBlockedEventHandler` consumer hooked (log-only until Phase 6).
- **Reads:** raw-ADO view queries in `ContentQueries`; keyset cursors `ThreadFeedCursor`/`ThreadSearchCursor` (pipe-delimited payload, Base64Url; search cursor carries `ts_rank`) — **no OFFSET**; FTS via `websearch_to_tsquery('simple')` (robust to raw user input; **no ILIKE**). 17 endpoint operations under `/api/content/*`, all visible in OpenAPI.
- **Guardrails updated:** `ModuleBoundaryTests` now permits cross-module `*.Contracts` while banning Domain/Application/Infrastructure/Presentation both ways; Content domain-purity rule added. Rate-limit permits configurable via `RateLimiting:{Global,Auth}:PermitLimit` (defaults 100/10) — `ForumApiFactory` raises them so suites don't 429.
- **Verified green (everything):** `dotnet build` · `dotnet format --verify-no-changes` · `dotnet test` = **57 tests** (26 Content unit, 20 Identity, 2 arch, 9 integration incl. 5 `ContentFlowTests` E2E: keyset paging without dupes + tags, depth-cap 422, `"[deleted]"` keeps children, FTS single hit, 403-for-plain-user vs moderator pin/delete + cursor across the pinned boundary).

**Phase 3 — Files: code-complete + fully verified (2026-07-03).** Module `Forum.Modules.Files` filled in (was a scaffold), already registered in `Program.cs`.
- **`forum_files`** — migration `InitialFiles` (EF): `files` (bucket+object_key unique, declared content_type/size_bytes, width/height, status text pending/committed, `uploaded_by` = `OwnerId` (IOwned) + full audit via AggregateRoot, **no soft-delete** — orphans are physically removed), `file_attachments` (PK file_id+target_type+target_id, `ix_attach_target`, cascade), `outbox_messages`; partial indexes `ix_files_pending_sweep`/`ix_files_committed_sweep` match the sweep predicates. Enums stored as text (Content precedent), NOT PG enums.
- **Flow (ADR 0008):** `POST /api/files` (initiate: any authenticated user, allow-list+max-size on declared values, pending row, ULID-month-sharded key `yyyy/MM/{ulid}`, presigned PUT) → client PUTs to MinIO → `POST /api/files/{id}/commit` (uploader-only; **stats real size + sniffs real type from magic bytes + decodes dimensions via hand-rolled `ImageProbe`** — PNG/JPEG/GIF/WebP header parsers, no ImageSharp: 4.x demands a paid build-time license key; declared values never trusted; idempotent re-commit). Reads: `GET /api/files/{id}` + `GET /api/files?targetType&targetId` — presigned GET, anonymous (public forum parity; short TTL). 6 endpoints, all in OpenAPI.
- **Attach authorization (key deviation from the "Content calls a Files port" idea — that direction would create a circular project reference, since Files already consumes Content's deletion events):** dependency stays one-way Identity ← Content ← Files. Files owns `POST/DELETE /api/files/{id}/attachments`; thread/comment/category_icon targets are gated by **Content's new Contracts surface `IContentAuthorization.AuthorizeAttachmentAsync`** (internal `ContentAttachmentAuthorizer`: existence 404 → owner-or-moderator-at-category 403, uses `IPermissionService` with explicit userId); avatar is self-authorized (target = current user), dm → 422 until Phase 5. Only the uploader may attach their file; avatar/category_icon get replace semantics, thread/comment additive with cap (`Files:MaxAttachmentsPerTarget`, 10). `users.avatar_file_id`/`categories.icon_file_id` sync deferred to Phase 6 (consume `FileCommitted`+attachments via relay).
- **Orphan sweep:** `OrphanSweeper` (Application, unit-testable) + `OrphanSweepService` **BackgroundService** (PeriodicTimer, first tick after one interval) — a recurring job, deliberately NOT `IStartupTask`; cross-replica dedupe via **Postgres session advisory lock** (`AdvisorySweepLock`, `ISweepLock` port). Removes blob-then-row (both idempotent): pending past `PendingGraceMinutes` (60) and committed-unattached past `UnattachedGraceMinutes` (1440), batch-capped; each removal enqueues `FileOrphanedIntegrationEvent`.
- **Events/consumers:** `FileCommitted`/`FileOrphaned` in `Contracts/IntegrationEvents` via module-local outbox writer; consumers for Content's `ThreadDeleted`/`CommentDeleted` detach attachments (real logic now, relay drives them in Phase 6; integration test invokes handler directly).
- **Shared infra extended:** `IObjectStorage` += `EnsureBucketAsync`/`PresignPutAsync`/`PresignGetAsync`/`StatAsync` (null when missing)/`ReadRangeAsync`/`RemoveAsync` (idempotent) + `ObjectStatResult`; `StorageOptions` untouched — Files policy lives in module-local `FilesOptions` ("Files" section in appsettings: TTLs, allow-list, max size 5 MiB, probe 128 KiB, graces, interval, batch, cap). `Testcontainers.Minio 4.1.0` pinned in CPM.
- **Guardrails:** `ModuleBoundaryTests` extended — Files may touch Identity/Content only via Contracts, upstream modules must not depend on Files at all, Files domain-purity rule. Files csproj has `InternalsVisibleTo` for its tests + `Forum.IntegrationTests` (drives sweeper/consumers until Phase 6).
- **Verified green (everything):** `dotnet build` · `dotnet format --verify-no-changes` · `dotnet test` = **104 tests** (40 Files unit incl. ImageProbe/StoredFile/sweeper/commit+attach handlers, 26 Content, 20 Identity, 2 arch, 16 integration incl. 7 `FilesFlowTests` E2E against real MinIO: presigned PUT→commit-with-dimensions→attach→presigned GET byte-identical→detach, size/type-mismatch 422s, commit-before-upload 409, foreign-thread attach 403, thread-delete→consumer-detach→sweep removes blob+row, abandoned pending swept). `ForumApiFactory` now also boots a MinIO container (parallel with Postgres), bootstraps the bucket via `EnsureBucketAsync`, zeroes both grace windows (sweep only runs when a test invokes it).

**Phase 4 — Engagement: code-complete + fully verified (2026-07-04, branch `9-feat-phase-4---engagement`).** Module `Forum.Modules.Engagement` filled in (was a scaffold), already registered in `Program.cs`.
- **`forum_engagement`** — migration `InitialEngagement` (EF): `reactions` (PK `(user_id, target_type, target_id, reaction_type)`; `reaction_type` text — only `'like'` implemented, but it sits in the key as the deliberate hook for future multi-kind reactions; `value smallint DEFAULT 1` is the SEPARATE signed-vote axis, extensible to -1; target_type text `thread|comment`, `ix_reactions_target`), `outbox_messages`. Migration `AddEngagementCountersAndViews` (raw SQL in `Infrastructure/Counters/EngagementCountersAndViews.cs`): **`reaction_counts` lives OUTSIDE the EF model and is maintained ONLY by an `AFTER INSERT OR DELETE` row trigger** (mirrors the `search_tsv` trigger precedent — counter can't drift no matter which code path writes: toggle, cascade consumers, future seeding; zeroed rows are deleted) + **`user_stats_v`** read-only cross-schema view (Engagement migrates last): live thread/comment counts + karma = `SUM(r.value)` over reactions on the user's live content.
- **Content untouched** (its views still show `0 AS like_count` placeholders — deliberate): no upstream schema/view/project-ref changes. Engagement resolves the like gate itself via `ContentTargetReader` — read-only ADO into `forum_content.threads/comments/categories` (the "later module read-joins earlier tables" precedent). Gate order mirrors CreateThread: target 404 → private-category owner-or-`moderate` 403 → **`Permissions.Like` at category scope** 403 (first use of the Phase 1 `like` bit). SPA composes feed + `/reactions/batch` itself (fetch-then-patch pattern).
- **Use cases** (Result): AddReaction/RemoveReaction — **idempotent both directions** (re-like / un-like-never-liked are 200 no-ops returning current `{count, viewerReacted}`), `ReactionAdded/Removed` via module-local outbox writer; GetReactionSummary + batch (≤100 ids, one round-trip two-resultset ADO query, zero-fill for unknown ids — **no existence check on reads by design**, keeps them pure PK lookups); GetUserStats (404 unknown user). 5 endpoints under `/api/engagement/*` (PUT/DELETE/GET `/reactions/{targetType}/{targetId}`, GET `/reactions/batch`, GET `/users/{id}/stats`; reads anonymous), all verified in OpenAPI against a live host. Consumers for `ThreadDeleted`/`CommentDeleted` bulk-`ExecuteDelete` reactions (row trigger folds counters; delete-then-consume drift window closes via relay in Phase 6).
- **Guardrails:** `ModuleBoundaryTests` extended — Engagement touches Identity/Content only via Contracts, must not touch Files, nobody may depend on Engagement, domain-purity rule. **`FilesFlowTests` consumer invocation switched to `GetServices<…>` loop** (drives ALL handlers like the bus): `GetRequiredService` would now resolve Engagement's `ThreadDeleted` handler, since it registers last.
- **Verified green (everything):** `dotnet build` · `dotnet format --verify-no-changes` · `dotnet test` = **134 tests** (24 Engagement unit incl. toggle idempotency/gate order/batch validation/consumers, 40 Files, 26 Content, 20 Identity, 2 arch, 22 integration incl. 6 `EngagementFlowTests` E2E: multi-user counts + double-like/unlike no-ops, batch summaries + 422 guardrails, private-category 403-for-intruder vs owner likes, 404s for missing/garbage targets, comment+thread delete cascades via consumers with counter checks, user_stats karma=2 and unknown-user 404).

**Next: Phase 5 Social is OPTIONAL (skip unless B builds it) → otherwise Phase 6 — Messaging backbone (RabbitMQ + outbox relay + consumers). Phase 4 work is uncommitted — review then commit.**
