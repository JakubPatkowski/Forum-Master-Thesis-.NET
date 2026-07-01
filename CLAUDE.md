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
- **Pending (Docker-blocked this session):** `AclSqlTests` + `IdentityFlowTests` need Postgres via Testcontainers — no container runtime would start here (see memory `container-runtime-unavailable`). Run `dotnet test Forum.slnx` once Docker is up to close the DoD. `dotnet ef` 10.0.2 pinned in `backend/.config/dotnet-tools.json`.

**Next: finish Phase 1 verification (integration tests under Docker), then Phase 2 — Content.**
