# forum-dotnet — Architecture A (React SPA + .NET 10)

Reference implementation of a forum for the master's thesis comparing a **decoupled React + .NET 10**
stack (this repo, *Architecture A*) against a **Go SSR monolith** (*Architecture B*). Both implement the
same functional specification (`../../docs/specs/`) so the comparison across the five research categories
(Resource Management · Rendering & Network Efficiency · Developer Velocity · Maintainability · Scalability)
is fair.

This repository is **self-contained**: backend, frontend, database, Kubernetes manifests, infrastructure,
scripts and CI all live here. Init `git` at this folder.

## Architecture

**Module-first modular monolith** on .NET 10 (hexagonal inside each module). The solution is organized by
**business module**, not by technical layer — the top level "screams" the domain. Each module is one project
containing its own `Domain / Application / Infrastructure / Presentation / Contracts` folders. Everything in a
module is `internal` except its `Contracts/` surface; modules talk to each other only through those contracts
(and integration events), never by reaching into internals. `Forum.ArchitectureTests` (NetArchTest) fails the
build if a module boundary or the in-module layer rule is violated. See `docs/architecture/adr/`.

```
backend/
  Forum.slnx
  Directory.Build.props · Directory.Packages.props · global.json   (repo-wide)
  src/
    Bootstrap/
      Forum.Api                  the single executable: Program.cs, auth, middleware, OTel, health, OpenAPI;
                                 discovers IModule implementations and wires them in
    Shared/
      Forum.SharedKernel         Entity, AggregateRoot, ValueObject, Result, Error, domain primitives
      Forum.Common               CQRS abstractions, IModule/IEndpoint, in-process event bus, paging, correlation
      Forum.Infrastructure       shared adapter base: EF Core base, outbox, RabbitMQ, MinIO, startup tasks
    Modules/
      Directory.Build.props      one baseline (frameworks + EF + validation + shared refs) for every module
      Identity/Forum.Modules.Identity      Domain/ Application/ Infrastructure/ Presentation/ Contracts/ + IdentityModule.cs
      Content/Forum.Modules.Content
      Files/Forum.Modules.Files
      Engagement/Forum.Modules.Engagement
  tests/
    Forum.ArchitectureTests      module boundaries + in-module layer rules (NetArchTest)
    Forum.Modules.Identity.Tests
    Forum.Modules.Content.Tests
    Forum.IntegrationTests       real Postgres via Testcontainers
    Forum.TestUtilities          shared fixtures
```

Inside a module (e.g. `Forum.Modules.Identity`): `Domain/` (aggregates, VOs, events — internal),
`Application/` (use-case handlers, ports, validators — internal), `Infrastructure/` (EF config, repositories,
ACL SQL — internal), `Presentation/` (Minimal API `IEndpoint`s — internal), `Contracts/` (the **only** public
surface: integration events + DTOs other modules may use), and `IdentityModule.cs` (`IModule`: registers DI +
maps endpoints + owns its migrations & schema).

Why module-first over layer-first: in a modular monolith the boundary that matters most is *between modules*,
and module-as-project makes a cross-module internal reference a **compile error**. In-module layer purity is the
lesser concern and is enforced by `ArchitectureTests`. Result: ~13 cohesive projects grouped into three folders
instead of a flat pile, and a module can later be extracted to a service as a unit. (ADR 0002.)

## Tech stack

| Concern | Choice |
|---|---|
| Runtime | .NET 10, C# latest, Minimal APIs |
| Persistence | EF Core 10 + Npgsql (PostgreSQL 17), schema-per-module, snake_case, NoTracking reads, pooled DbContext |
| Read model | SQL **views** + view repositories (avoid N+1); **keyset** pagination; **FTS** (tsvector) |
| AuthN | JWT access (15 min) + refresh (14 d) with **rotation + reuse-detection**; refresh in httpOnly cookie |
| AuthZ | RBAC + **bitmask ACL resolved in SQL** (`docs/db/permissions-acl-design.md`); Argon2id hashing |
| Messaging | RabbitMQ + **transactional outbox**; in-process event bus between modules; WebSocket for realtime |
| Storage | MinIO (S3) via presigned/proxied upload |
| Observability | Serilog → Loki; **OpenTelemetry** traces → Tempo; metrics → Prometheus (`/metrics`); Grafana |
| Validation | FluentValidation; errors as `Result`/`Error` → consistent HTTP envelope |
| Packaging | Central Package Management (`Directory.Packages.props`) for CVE auditing |
| Deploy | Docker (multi-stage, non-root) → Kubernetes (minikube): HPA, PDB, NetworkPolicy, migration **Job** |
| CI | GitHub Actions: restore, `dotnet format` check, build, test; weekly vulnerable-package scan |

## Quick start

Runs on **WSL2 (Ubuntu) or Linux** with the repo on the Linux filesystem (not a
`/mnt` mount). `make help` lists everything; `make preflight` checks the toolchain.

```bash
cp .env.example .env
make preflight            # docker / kubectl / minikube / dotnet / node / k6

# Local inner loop (containers for infra, dotnet for the API, Next.js for the SPA):
make infra-up             # Postgres + RabbitMQ + MinIO
make api ARGS=--migrate   # migrate + run Forum.Api on :8080
make web                  # npm install (if needed) + frontend dev server on :3000

# Cluster (minikube):
make mk-up                # start the cluster
make mk-deploy            # build image into minikube + apply manifests
make urls
```

`make web` runs against `make api` (`:8080`); see `frontend/README.md` for the alternative
plain-`dotnet run` + `npm run dev` workflow, which lands on `:5099` instead.

Without `make`, call the scripts directly (`bash scripts/<name>.sh`).
Full walkthrough incl. the WSL move: **`docs/runbooks/wsl-minikube-setup.md`**.

## Status

Scaffold: structure, project graph (13 projects), shared kernel, module installers (`IModule`), host wiring,
infra, CI, ACL design and ADRs are in place. Module implementation order: **Identity → Content → Files →
Engagement**. `// TODO` markers indicate wiring points. Run `dotnet restore && dotnet build` and verify
centrally-pinned package versions on first restore.
