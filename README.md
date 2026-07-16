# forum-dotnet - Architecture A (React SPA + .NET 10)

> Professional, module-first forum platform built for a master's thesis benchmark: **Architecture A (this repo)** vs **Architecture B (Go SSR monolith)** under one shared functional contract.

<!-- TODO: Badges -->
[![Build](#)](#)
[![Tests](#)](#)
[![Coverage](#)](#)
[![License](#)](#)
[![Kubernetes](#)](#)

---

## Table of contents

- [What this repository is](#what-this-repository-is)
- [Architecture at a glance](#architecture-at-a-glance)
- [Key technical decisions](#key-technical-decisions)
- [Repository structure](#repository-structure)
- [Backend highlights](#backend-highlights)
- [Kubernetes & operations highlights](#kubernetes--operations-highlights)
- [Observability](#observability)
- [Security model](#security-model)
- [Current implementation status](#current-implementation-status)
- [Quick start (local)](#quick-start-local)
- [Quick start (minikube)](#quick-start-minikube)
- [Monitoring stack](#monitoring-stack)
- [Testing & quality gates](#testing--quality-gates)
- [Benchmarking](#benchmarking)
- [Documentation map](#documentation-map)
- [Placeholders for visuals](#placeholders-for-visuals)

---

## What this repository is

`forum-dotnet` is the complete implementation of **Architecture A**:

- **Frontend:** decoupled React SPA (Next.js app shell, pure CSR data flow)
- **Backend:** .NET 10 modular monolith
- **Infra:** PostgreSQL, RabbitMQ, MinIO, Kubernetes manifests, scripts, CI
- **Thesis goal:** compare A vs B fairly on performance, scalability, maintainability, and developer velocity

The shared comparison scope is intentionally fixed and documented so both architectures implement equivalent forum functionality.

---

## Architecture at a glance

### Core style

- **Module-first modular monolith**
- **Hexagonal inside each module** (`Domain / Application / Infrastructure / Presentation / Contracts`)
- **Single executable host:** `Forum.Api`
- **Hard boundaries:** modules communicate only via `*.Contracts` and integration events

### Data and integration style

- **Schema-per-module** in PostgreSQL
- **No cross-schema foreign keys**
- **Reads:** SQL views + keyset pagination + FTS
- **Writes:** aggregates + unit of work + Result pattern
- **Cross-module async:** RabbitMQ + transactional outbox + inbox dedupe

### Realtime style

- **Fetch-then-patch model**: SPA loads view, then applies WS change notifications
- **No polling**
- **Replica-safe fan-out** through RabbitMQ-backed feed service

---

## Key technical decisions

- **ULID everywhere** (all IDs, URLs, contracts)
- **Argon2id** password hashing
- **JWT access + rotating refresh tokens** with reuse detection
- **RBAC + SQL bitmask ACL** resolved in PostgreSQL (`int_or_agg`, `effective_mask`, cache)
- **Direct-to-MinIO uploads** via presigned URLs (backend is control plane, not byte proxy)
- **Migrations as Kubernetes Job** (never at app startup)
- **Structured observability**: Serilog JSON -> Loki, OTel traces -> Tempo, metrics -> Prometheus/Grafana

ADRs: `docs/architecture/adr/0001`-`0010`.

---

## Repository structure

```text
backend/
  src/
    Bootstrap/Forum.Api
    Shared/
      Forum.SharedKernel
      Forum.Common
      Forum.Infrastructure
    Modules/
      Identity/Forum.Modules.Identity
      Content/Forum.Modules.Content
      Files/Forum.Modules.Files
      Engagement/Forum.Modules.Engagement
  tests/
    Forum.ArchitectureTests
    Forum.Modules.*.Tests
    Forum.IntegrationTests
    Forum.Api.Tests

frontend/                  # Next.js app shell, CSR-only data access
k8s/                       # app manifests (hand-rolled YAML)
infrastructure/monitoring/ # monitoring architecture notes
scripts/                   # deploy, setup, tunnels, monitoring, benchmark helpers
docs/                      # authoritative architecture + runbooks
```

---

## Backend highlights

- **CQRS without MediatR** (Scrutor-based registration, explicit handlers)
- **Result/Error model** with deterministic API mapping (`404 -> 403 -> 422` precedence)
- **Audit + ownership + soft-delete** as shared primitives
- **Deterministic seeds** (Development and Benchmark profiles)
- **Integration pipeline**:
  1. write aggregate + outbox in one transaction
  2. relay publishes with confirms
  3. consumer idempotency via inbox PK
- **Realtime ticket flow**: short-lived single-use WS ticket, secure browser handshake

---

## Kubernetes & operations highlights

- **minikube profile with Calico CNI** (NetworkPolicy enforcement is real, not cosmetic)
- **PSS-restricted app namespace** (`forum-dotnet`)
- **Tokenless ServiceAccounts** (no unnecessary API credentials mounted into pods)
- **Hardened workloads** (non-root, seccomp, dropped capabilities, read-only rootfs where valid)
- **Ingress + TLS** (`forum.local`, `minio.forum.local`, `grafana.forum.local`)
- **NetworkPolicies** for backend/postgres/rabbitmq/minio/frontend segmentation
- **Deterministic deploy order** in `scripts/deploy.sh`:
  secrets -> infra -> jobs (bucket/migrate/seed) -> backend/frontend -> ingress -> netpol
- **Connection-pool math locked** (`replicas x pool <= max_connections`)

---

## Observability

- `/metrics`, `/health/live`, `/health/ready`
- Test-verified log shape and correlation keys (`@tr`, `CorrelationId`)
- Domain metrics include:
  - auth outcomes
  - content/reaction counters
  - outbox publish lag/failures
  - messaging outcomes
  - websocket connections/subscriptions/pushes
  - API rejection/error classification
  - background-loop liveness gauge
- Monitoring stack (Helm, pinned versions): kube-prometheus-stack + Loki + Alloy + Tempo + postgres-exporter
- Grafana dashboards shipped as code + query index (`k8s/monitoring/QUERIES.md`)

---

## Security model

- JWT + refresh rotation + family reuse detection
- Argon2id password hashing
- SQL-native ACL checks (RBAC + bitmask)
- CORS allow-list (no wildcard + credentials)
- Rate limiting + forwarded-header trust boundaries
- Presigned object access with private buckets
- Secrets only via user-secrets / Kubernetes Secret (never in tracked appsettings)

---

## Current implementation status

Implemented and verified:

- [x] Phase 0: foundation & shared abstractions
- [x] Phase 1: Identity + Authz
- [x] Phase 2: Content
- [x] Phase 3: Files
- [x] Phase 4: Engagement
- [x] Phase 6: messaging backbone (outbox + RabbitMQ + inbox)
- [x] Phase 7: realtime WebSocket
- [x] Phase 8: frontend SPA
- [x] Phase 9a: backend observability finalization
- [x] Phase 9b: deterministic seed profiles
- [x] Phase 10b: Kubernetes core hardening
- [x] Phase 10c: monitoring stack
- [x] Phase 10d: performance/caching decisions and optimizations (**code complete, benchmark re-run pending**)

Planned next:

- [ ] Phase 9c: k6 measured benchmark runs + archived results
- [ ] Optional Phase 10e: Social module (go/no-go based on parity/time)

---

## Quick start (local)

```bash
cp .env.example .env
make preflight
make infra-up
make api ARGS=--migrate
make web
```

Useful:

```bash
make test
make format
make infra-down
```

---

## Quick start (minikube)

```bash
make mk-up
make mk-tls              # one-time cert generation
make mk-deploy ARGS=--seed
make pods
make tunnels
```

Cluster reset / reseed:

```bash
make mk-reset-db
make seed ARGS=--benchmark
```

Detailed Windows/WSL access model: `docs/runbooks/wsl-minikube-setup.md`.

---

## Monitoring stack

```bash
make mon-up
make mon-check
make mon-down
```

Grafana and Prometheus tunnels are integrated into `make tunnels` when monitoring namespace exists.

---

## Testing & quality gates

Backend:

- `dotnet build`
- `dotnet format --verify-no-changes`
- `dotnet test` (unit + architecture + integration + API tests)

Frontend:

- `npm run typecheck`
- `npm run lint`
- `npm test`
- `npm run build`

Architecture boundaries are enforced in `Forum.ArchitectureTests`.

---

## Benchmarking

- k6 profiles and benchmark workflow are defined for thesis-grade reproducibility
- deterministic seed profile for benchmark DB: `forum_net_bench`
- observability queries are reusable from dashboard source (`k8s/monitoring/QUERIES.md`)
- current open benchmark task: post-optimization before/after archival run (Phase 9c + 10d closeout)

---

## Documentation map

Start here:

- `docs/architecture/REQUIREMENTS-AND-ASSUMPTIONS.md` (master assumptions)
- `docs/architecture/DOMAIN-MODEL-AND-DATABASE.md` (schema rules)
- `docs/architecture/FOUNDATION-AND-SHARED-ABSTRACTIONS.md` (shared base)
- `docs/architecture/IMPLEMENTATION-PLAN.md` (phase flow)
- `docs/architecture/PHASE-9-10-ENTERPRISE-PLAN.md` (enterprise/ops phases)
- `docs/architecture/OBSERVABILITY-CONTRACT.md` (metric/log/tracing contract)
- `docs/db/permissions-acl-design.md` (ACL SQL design)
- `docs/runbooks/wsl-minikube-setup.md` (WSL2 + minikube operational runbook)

---

## Placeholders for visuals

<!-- TODO: Architecture diagram -->
### Architecture diagram

`[PLACEHOLDER: module-first architecture diagram here]`

<!-- TODO: Deployment topology -->
### Kubernetes topology

`[PLACEHOLDER: k8s topology diagram here]`

<!-- TODO: Monitoring screenshots -->
### Monitoring screenshots

`[PLACEHOLDER: Grafana dashboard screenshots here]`

<!-- TODO: App screenshots -->
### Product screenshots

`[PLACEHOLDER: SPA screens (feed/thread/realtime/upload) here]`

---

## Notes

- I could not access the external file path `c:/Users/jakub/Downloads/README (7).md` from this environment, so this README was built from the repository's current docs and implementation state.
- This update intentionally modifies **only** `README.md`.
