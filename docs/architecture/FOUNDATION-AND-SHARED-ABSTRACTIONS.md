# Foundation & Shared Abstractions (Phase 0)

> **Status:** Phase 0 complete — `dotnet build` / `dotnet test` / `dotnet format --verify-no-changes` green on .NET 10; `/health/live` smoke test passes; `Forum.ArchitectureTests` green.
> **Scope:** the load-bearing building blocks every module inherits. **No domain entities yet** (those start in Phase 1).
> **Read with:** [`IMPLEMENTATION-PLAN.md`](./IMPLEMENTATION-PLAN.md) (Phase 0 block), [`REQUIREMENTS-AND-ASSUMPTIONS.md`](./REQUIREMENTS-AND-ASSUMPTIONS.md), ADRs 0002–0010.

This document catalogues **every shared abstraction** introduced in Phase 0 and **every assumption / decision** baked into the foundation, so later phases extend it instead of re-deriving it.

---

## 1. Build & tooling assumptions

| Decision | Detail / rationale |
|---|---|
| **.NET 10**, C# latest, nullable + implicit usings | Set once in `backend/Directory.Build.props`; all projects inherit. |
| **Central Package Management** | Every version pinned in `backend/Directory.Packages.props`; **never** a `Version=` in a `.csproj`. CVE auditing + no drift. |
| **`Directory.Build.props` is the single style/quality gate** | `TreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`, `AnalysisLevel=latest-recommended`, `GenerateDocumentationFile=true` (XML docs required except `CS1591` muted; test projects exempt). |
| **NuGet audit is non-blocking** | `WarningsNotAsErrors=NU1901;NU1902;NU1903;NU1904` — a newly disclosed upstream CVE shows as a warning, never breaks the build; the weekly `security.yml` workflow surfaces them. |
| **Analyzer carve-outs** (`.editorconfig`) | `CA2007` off (no `ConfigureAwait` ceremony in app code); `CA1716` off (`Error` is a deliberate Result type); `CA1711` off (`IDomainEventHandler` is a DDD interface, not a CLR delegate); `CA1848` → suggestion (no `LoggerMessage` ceremony for cold paths); `CA1707` off **for tests only** (`Method_Scenario_Expectation` names); accessibility modifiers required for non-interface members only. |
| **Line endings** | `.editorconfig` `end_of_line=lf`; `.gitattributes` normalises to LF on commit (`*.sh` LF, `*.ps1` CRLF). **Never put `--` inside an XML comment** in build files — it silently disables CPM. |
| **Test helper ≠ test project** | `Forum.TestUtilities` sets `<IsTestProject>false</IsTestProject>` so `dotnet test` does not try to run it. |
| **CI** | `ci.yml` (restore → format check → build → test) on `master`; `security.yml` weekly vulnerable-package scan. |

---

## 2. `Forum.SharedKernel` — domain primitives (framework-free)

Pure domain building blocks. **No EF/ASP.NET/MSBuild references** — enforced by `Forum.ArchitectureTests` Domain-purity rule.

| Type | Purpose |
|---|---|
| `Entity<TId>` | Identity-based equality (`==`, `Equals`, `GetHashCode`); EF-friendly protected ctor. |
| `AggregateRoot<TId>` | The only mutation entry point. Records domain events (`Raise` / `DomainEvents` / `ClearDomainEvents`) **and** carries audit columns (`CreatedOnUtc/By`, `LastModifiedOnUtc/By`) via `SetCreated`/`SetModified` (stamped by the interceptor, never by hand). Implements `IAuditableEntity` + `IHasDomainEvents`. |
| `ValueObject` | Structural equality base for value objects. |
| `IDomainEvent` | A fact raised inside an aggregate (`OccurredOnUtc`); dispatched **after** commit. |
| `IDomainEventHandler<TEvent>` | In-process handler for a domain event (`Handle(event, ct)`). |
| `IHasDomainEvents` | Non-generic view (`DomainEvents`, `ClearDomainEvents`) so persistence can collect events regardless of the id type. |
| `IAuditableEntity` | `CreatedOnUtc/By`, `LastModifiedOnUtc/By` + `SetCreated`/`SetModified`. |
| `ISoftDeletable` | `IsDeleted`, `DeletedOnUtc`, `DeletedBy`, `MarkDeleted(...)`; a global query filter hides flagged rows. |
| `IOwned` | `OwnerId` — ownership gates author-vs-`*.any` authorization. |
| `Result` / `Result<T>` | Outcome of an operation; `Success`/`Failure`; implicit conversions from value/`Error`. **No exceptions for expected failures.** |
| `Error` / `ErrorType` | Typed error (`Code`, `Description`, `Type`); `ErrorType` ∈ `Failure/Validation/NotFound/Conflict/Unauthorized/Forbidden` → mapped to HTTP at the edge (order **404 → 403 → 422**). |

---

## 3. `Forum.Common` — application & cross-cutting contracts

| Type | Purpose |
|---|---|
| `ICommand`, `ICommand<TResponse>`, `IQuery<TResponse>` | CQRS markers (handlers registered via Scrutor scan in later phases). |
| `IModule` | A self-contained vertical module: `Name`, `RegisterServices(services, config)`, `MapEndpoints(endpoints)`. The host discovers each one. |
| `IEndpoint` | One REST endpoint (1 file = 1 endpoint), mapped by its module. |
| `IEventBus` | In-process publish of **integration events** across modules (`PublishAsync<TEvent>`). Modules never call each other directly. |
| `IIntegrationEvent` | A durable cross-module fact (`EventId`, `OccurredOnUtc`); persisted via the outbox. |
| `IIntegrationEventHandler<TEvent>` | Reacts to an integration event (`HandleAsync`). |
| `PagedResult<T>` | Offset page (admin lists). |
| `CursorPage<T>` | Keyset/cursor page (`NextCursor`, `HasMore`) — preferred for hot, deep lists. |
| `ICorrelationContext` | Per-request correlation id (`CorrelationId`, `Set`). |
| `ICurrentActor` | The acting principal (`Ulid? Id`); null when unauthenticated (system jobs, migrations, anonymous). |

---

## 4. `Forum.Infrastructure` — shared adapters

| Type | Purpose |
|---|---|
| `ForumDbContext` | Base every module DbContext inherits: no-tracking reads by default, **global soft-delete query filter** (auto-applied to any `ISoftDeletable`), and `SaveChangesAndDispatchEventsAsync` (persist, then dispatch domain events of touched aggregates). |
| `AuditInterceptor` | `SaveChanges` interceptor stamping `IAuditableEntity` rows on insert/update using `TimeProvider` + `ICurrentActor`. |
| `IDomainEventDispatcher` / `DomainEventDispatcher` | Resolves and invokes registered `IDomainEventHandler<T>` per event (in-process, within the request scope). |
| `InMemoryEventBus` | Phase-0 `IEventBus`: publishes to in-process `IIntegrationEventHandler<T>`. **Replaced by the RabbitMQ outbox relay in Phase 6** — modules keep the same `IEventBus` API. |
| `OutboxMessage` + `OutboxMessageConfiguration` | Transactional-outbox row (`Id/Type/Payload/OccurredOnUtc/ProcessedOnUtc/Error`) mapped to a per-module `outbox_messages` (`jsonb` payload, index on `ProcessedOnUtc`). |
| `RabbitMqOptions` / `IRabbitMqConnection` / `RabbitMqConnection` | Lazy shared connection (opened on first use, **never at boot**), so a missing broker can't block startup or liveness. Consumers wired in Phase 6. |
| `StorageOptions` / `IObjectStorage` / `MinioObjectStorage` | MinIO/S3 wrapper; client created eagerly but opens no connection until called. Presigned URLs land in Phase 3. |
| `AddForumInfrastructure(config)` | DI entry point: `TimeProvider`, default `ICurrentActor`, `AuditInterceptor`, dispatcher, `IEventBus`, RabbitMQ + MinIO. |
| `ModuleDbContextRegistration.AddModuleDbContext<TContext>` | Registers a module context with Npgsql + **snake_case** + the audit interceptor, and exposes it to the migration runner. |
| `IStartupTask` / `StartupTaskRunner` | Ordered boot work (migrations/views/seed); `RunWithStartupTasksAsync` runs them then serves. |
| `MigrationRunner.RunMigrationsAsync` | Applies every registered context's migrations then exits (the `migrate` entrypoint; a one-shot k8s Job per ADR 0005). No-op until a module registers a context. |

---

## 5. `Forum.Api` — host wiring (the only executable)

`Program.cs` discovers `IModule`s and builds this request pipeline (order matters):

`UseExceptionHandler` → `CorrelationIdMiddleware` → Serilog request logging → security headers → (dev) OpenAPI → CORS → rate limiter → authentication → authorization → health/metrics/module endpoints.

| Piece | Detail |
|---|---|
| `CorrelationContext` + `CorrelationIdMiddleware` | Honours/echoes `X-Correlation-ID`, pushes it to Serilog `LogContext`. |
| `AddForumProblemDetails` + `GlobalExceptionHandler` | Unhandled exceptions → RFC 7807 ProblemDetails (expected failures use `Result`, not exceptions). |
| `AddForumCors` | Explicit SPA origin allow-list from `Cors:AllowedOrigins` (never wildcard + credentials). |
| `AddForumRateLimiting` | Baseline per-IP fixed window (100/min); tighter per-endpoint limits in Phase 1. |
| `AddForumAuthentication` | JWT bearer (issuer/audience/lifetime) + authorization **skeleton**; signing key, token issuance and the httpOnly refresh cookie arrive in Phase 1. |
| Observability / health / security headers | OpenTelemetry traces+metrics (+ `/metrics`), Serilog, `/health/{live,ready}`, baseline hardening headers. |
| `migrate` arg hook | `dotnet Forum.Api.dll migrate` runs migrations and exits (no-op until Phase 1). |

---

## 6. Tests

| Project | Contents |
|---|---|
| `Forum.ArchitectureTests` | NetArchTest rules: modules talk only via `*.Contracts`; module `Domain` stays free of Infrastructure/Presentation/EF/ASP.NET. **The guardrail — keep it green.** |
| `Forum.TestUtilities` | `PostgresFixture` (Testcontainers) for DB-backed tests. Helper library (`IsTestProject=false`). |
| `Forum.IntegrationTests` | `HealthCheckTests` smoke test: boots the whole Host via `WebApplicationFactory<Program>` and asserts `/health/live` = 200 with **no** DB/broker/storage available. |
| `Forum.Modules.{Identity,Content}.Tests` | Empty scaffolds; filled from Phase 1. |

---

## 7. Invariants every module inherits (Phase 0 assumptions)

1. **Result pattern** for expected failures (no exceptions); HTTP mapping at the edge, validation order **404 → 403 → 422**.
2. **ULID** ids; **audit** columns on every aggregate root (interceptor-stamped); **soft-delete** filter on `ISoftDeletable`; **ownership** via `IOwned`.
3. **Reads:** SQL views + **keyset** pagination (no OFFSET, no N+1). **Writes:** aggregates + repository + the DbContext as unit of work.
4. **Module isolation:** cross-module only via `*.Contracts` + integration events; **no cross-schema FK** (logical ULID links kept consistent by events); everything `internal` except `Contracts/`.
5. **Domain events** dispatched after commit in-process; **integration events** go through the outbox (relayed to RabbitMQ from Phase 6).
6. **Schema-per-module** DbContext, snake_case, no-tracking reads, audit interceptor — all from `AddModuleDbContext`.
7. **Migrations** run via a one-shot **k8s Job** (`migrate` arg), never at pod startup (ADR 0005).
8. **Infra connections are lazy** — the app boots and stays live without DB/RabbitMQ/MinIO present (critical for isolated, reproducible benchmarking).
9. **English** for code/identifiers/comments; secrets via user-secrets/k8s Secret, never in `appsettings*.json` or git.

---

## 8. Deliberately deferred (not in Phase 0)

Concrete entities/schemas, RBAC + SQL bitmask ACL (Phase 1), the JWT signing key + token issuance + refresh-cookie rotation (Phase 1), the outbox **relay** + consumers (Phase 6), the WebSocket hub (Phase 7), and real readiness checks wired to DB/RabbitMQ/MinIO.

**Next: Phase 1 — Identity + Authz.**
