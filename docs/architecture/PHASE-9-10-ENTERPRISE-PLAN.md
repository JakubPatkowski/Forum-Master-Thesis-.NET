# Phase 9–10 — Enterprise Plan: seed · benchmark · observability · Kubernetes hardening

> **Status:** authoritative for Phases 9–10 · **Supersedes** the Phase 9 and Phase 10 blocks of
> [`IMPLEMENTATION-PLAN.md`](./IMPLEMENTATION-PLAN.md) (they remain as historical context; THIS document is
> what a coding session executes) · **Updated:** 2026-07-07 · **Language:** English (repo convention)
>
> **Read first:** root `CLAUDE.md` (current state), [`REQUIREMENTS-AND-ASSUMPTIONS.md`](./REQUIREMENTS-AND-ASSUMPTIONS.md)
> §7–§9, ADRs [0005](./adr/0005-migrations-as-k8s-job.md)/[0008](./adr/0008-direct-to-minio-presigned-uploads.md)/
> [0009](./adr/0009-rabbitmq-inter-module-events.md)/[0010](./adr/0010-websocket-realtime-aggregate-changes.md),
> [`../runbooks/wsl-minikube-setup.md`](../runbooks/wsl-minikube-setup.md).
>
> **How to use this with Claude.** Same convention as `IMPLEMENTATION-PLAN.md`: each block below is
> self-contained (**Goal · Depends on · Steps · Watch out · Definition of Done · START-OF-PHASE REMINDERS**)
> and sized for one work session. Say e.g. *"Start Phase 10b — read its START-OF-PHASE REMINDERS first."*
> Every block assumes the global rules from `IMPLEMENTATION-PLAN.md` (Result pattern, CPM, module isolation,
> ArchitectureTests green, secrets never in git) still apply — they are not repeated per block.
>
> **Reference implementation.** Parts of this plan are informed by the author's earlier
> [`Python-Forum-API`](https://github.com/JakubPatkowski/Python-Forum-API) repo, which already ran a very
> similar stack (minikube + kube-prometheus-stack + Loki + NetworkPolicies + k6 smoke/demo/stress) and whose
> Helm values contain hard-won, documented failure diagnoses (Grafana CPU-throttle restarts, Loki OOM under k6
> log floods, cAdvisor TLS scrape config, `serviceMonitorSelectorNilUsesHelmValues`). Where that repo did
> something well, this plan adopts it; where it cut corners (Loki via the deprecated `loki-stack` chart,
> Promtail as shipper, k6 GET-only traffic, MinIO S3 API open to `0.0.0.0/0`), this plan deliberately does
> better and says so inline.

---

## Table of contents

- [§0 Verified current state & gap register (G1–G22)](#0-verified-current-state--gap-register)
- [§1 The 12 GiB resource contract (single budget table)](#1-the-12-gib-resource-contract)
- [§2 Target topology](#2-target-topology)
- [Phase 9a — Backend observability finalization (code)](#phase-9a--backend-observability-finalization-code)
- [Phase 9b — Deterministic seed](#phase-9b--deterministic-seed)
- [Phase 9c — k6 load profiles + benchmark runbook](#phase-9c--k6-load-profiles--benchmark-runbook)
- [Phase 10a — Docker image pipeline & hardening](#phase-10a--docker-image-pipeline--hardening)
- [Phase 10b — Kubernetes core: infra manifests, security, networking](#phase-10b--kubernetes-core-infra-manifests-security-networking)
- [Phase 10c — Monitoring stack (Helm, dashboards, alerts, correlation)](#phase-10c--monitoring-stack-helm-dashboards-alerts-correlation)
- [Phase 10d — Performance & caching (the Redis verdict)](#phase-10d--performance--caching-the-redis-verdict)
- [Phase 10e — Optional Social module (go/no-go)](#phase-10e--optional-social-module-gono-go)
- [§11 Scripts & Makefile inventory (cross-phase)](#11-scripts--makefile-inventory-cross-phase)
- [§12 Full bring-up runbook (cold machine → benchmark-ready)](#12-full-bring-up-runbook-cold-machine--benchmark-ready)
- [§13 CLAUDE.md updates needed](#13-claudemd-updates-needed)

**Recommended execution order** (dependency-driven, not numeric):

```
9a (backend code: metrics/tracing/forwarded-headers/presign-endpoint/JWT guard)
→ 9b (seed code + Job)
→ 10a (images: backend hardened, frontend created, tagging, trivy)
→ 10b (cluster: infra manifests, secrets, RBAC/PSS, NetworkPolicies, TLS, deploy.sh)
→ 10c (monitoring stack + dashboards + alerts)
→ 9c  (k6 profiles + the measured benchmark runs)          ← produces the thesis numbers
→ 10d (optimization iteration, re-measure)
→ 10e (optional Social — only if time + B parity)
```

9a and 9b are pure backend code and testable against `docker compose` — do them first so every later
cluster session deploys an image that is already observable and seedable.

---

## AMENDMENTS & USER FEEDBACK (2026-07-10)

> **Author:** Jakub Patkowski (2026-07-10) · **Status:** Suggestions for Phase 9b refinement
> · **Openness:** These are user-driven pragmatic adjustments; Fable 5 is invited to propose more
> professional/scalable variants if they improve the approach — the goal is a **realistic, maintainable**
> dev + benchmark environment, not a strangled-by-perfection design.

### A1: Dual Seed Profiles (Development vs Benchmark)

**Observation:** Original Phase 9b assumed one monolithic seed (2000 users, 10k threads) tightly coupled
to the benchmark flow. This conflates two separate needs: (1) rapid local development (needs a *small*,
*fast* seed for `make api` in <10 seconds), and (2) fair performance comparison with Architecture B (needs
a *large*, *deterministic, reproducible* dataset matching B's scale). A single seed harms both.

**Suggestion:** Introduce **two named profiles** in the seeder configuration:

| Profile | Users | Categories | Tags | Threads | Comments | Reactions | DB size | Seed time | Use case |
|---|---|---|---|---|---|---|---|---|---|
| **Development** | 5 | 2 | 4 | 10 | 10 | 0–5 | ~10 MB | <5 s | `make api` (hy day-to-day testing) |
| **Benchmark** | ≤500–1000* | 10–15 | 50–100 | 1000–2000 | 5000–10000 | 10000–20000 | ~200–400 MB | ~30–60 s | Measured thesis runs |

**\* Why reduced Benchmark numbers:** WSL2 12 GiB budget (§1) and 6 vCPU allocation leave ~500 MB for the
test DB before hitting memory pressure during k6 at 150 VU. The original 2000 users / 10k threads / 60k
comments dataset is **real-world-scale** (appropriate for a production thesis), but is **wasteful for a
minikube benchmark run** where:
- k6 setup() logs in 200 of N users anyway (the others never execute).
- Keyset pagination and FTS correctness are **NOT** a function of dataset size — they're tested in Phase 2–4 integration tests.
- Reaction counter trigger logic is **NOT** a function of reaction count — 20k reactions fire the same trigger paths as 120k.
- The thesis evaluates **Architecture A vs B throughput under identical load**, NOT "does A handle 60k comments" — that's a capacity question answered separately.

Reduced numbers still exercise the load: k6 `stress` at 150 VU against 1000 threads = hot-thread Zipf,
comment-tree recursion on real data, FTS with corpus hits.

**Implementation notes:**
1. `SeedProfile enum { Development, Benchmark }` in `Forum.Infrastructure/Seeding/`.
2. **One seeder implementation per module**, with profile-conditional counts:
   ```csharp
   var (userCount, catCount, threadCount, commentCount) = config.Profile switch
   {
       SeedProfile.Development => (5, 2, 10, 10),
       SeedProfile.Benchmark => (750, 15, 1500, 8000),    // ← concrete numbers TBD with Fable
       _ => throw new ArgumentException()
   };
   ```
3. Both profiles use **identical ULID seeding logic** (fixed RNG seed + timestamp base) → identical
   ordering across runs, deterministic keyset pagination.
4. **Determinism holds for both profiles:** whether you run Development or Benchmark, the first 5 users
   have the same IDs and email patterns (`bench_user_0001@bench.local`, etc.), same Thread/Comment ULIDs.

**Openness to Fable:** If numbers need adjustment after initial benchmarking (e.g., k6 sampler data shows
we're memory-bound at 500 users), a simple constant edit in the profile definition adjusts all modules
atomically. Propose concrete numbers based on your k6 stress-run profile peaks.

### A2: Database Isolation (Separate POSTGRES_DB per profile)

**Observation:** Running Development seed into the same `forum_net` database as Benchmark creates coupling:
- Load tests write new comments/reactions → dev data becomes stale/polluted.
- Resetting to Benchmark seed requires manual intervention.
- Tests (already isolated via Testcontainers) don't interfere, but local dev and local benchmark do.

**Suggestion:** `compose.yaml` already defaults `POSTGRES_DB` to `forum_net` — keep that as-is for
Development (zero disruption to the existing dev loop/docs) and introduce a second name, `forum_net_bench`,
for the Benchmark profile only:

```yaml
# compose.yaml
services:
  postgres:
    environment:
      POSTGRES_DB: ${POSTGRES_DB:-forum_net}  # override in Makefile
```

```bash
# Makefile
api:  ## Start dev API (Development seed)
	@$(COMPOSE_UP) && \
	$(DOCKER_EXEC) api dotnet run -- seed && \
	$(DOCKER_EXEC) api dotnet run --project src/Bootstrap/Forum.Api

bench-local:  ## Benchmark locally (Benchmark seed, isolated DB)
	@POSTGRES_DB=forum_net_bench $(COMPOSE_DOWN) && \
	POSTGRES_DB=forum_net_bench $(COMPOSE_UP) && \
	POSTGRES_DB=forum_net_bench $(DOCKER_EXEC) api dotnet run -- seed --benchmark --force && \
	POSTGRES_DB=forum_net_bench k6 run load/k6/main.js -e PROFILE=stress
```

**Result:** `make api` uses `forum_net`, `make bench-local` uses `forum_net_bench`. PostgreSQL on the
host handles multiple databases transparently; no schema conflicts. Tidy separation.

**Kubernetes implication:** The k8s Job `seed-job.yaml` runs with `args: ["seed"]` (Development, for
manual exploration) or `args: ["seed", "--benchmark"]` (for measured runs). Same POSTGRES_DB ConfigMap
key points to the cluster database (single minikube Postgres); the profile argument controls seed volume.

### A3: Test Isolation Remains Unchanged

**Reassurance:** Integration tests (`Modules.X.Tests` + `IntegrationTests`) continue to use Testcontainers
with **their own ephemeral Postgres container per test session**. They do NOT consume the Development or
Benchmark seeds; they seed themselves (small micro-seeds, 2–5 rows per test). Zero interference.

Example (no change from Phase 4):
```csharp
[Test]
public async Task ThreadKeyset_NoDuplicatesAcrossPages()
{
    // ForumApiFactory creates its own Testcontainers.PostgresFixture
    // (fresh, empty DB)
    await _apiFactory.Db.Threads.AddRangeAsync(
        Enumerable.Range(0, 50).Select(i => new Thread { ... })
    );
    await _apiFactory.Db.SaveChangesAsync();
    
    // Test keyset paging
    var page1 = await _api.GetAsync($"/api/content/threads?categoryId={cat}&limit=20");
    // ...
}
```

Testy wyróżniają się w `dotnet test` niezależnie od tego, czy Development czy Benchmark dane istnieją w
compose. Dokumentuj to w Phase 9b START-OF-PHASE REMINDERS.

---

## 0. Verified current state & gap register

Inspected on 2026-07-07, branch `15-feat-phase-8---frontend`. **Everything below was verified against the
actual files, not assumed.** Later blocks reference gaps by ID (G1…G22).

### What exists and is good (do not rebuild)

| Area | State |
|---|---|
| `Dockerfile` (root) | Multi-stage SDK→aspnet 10.0, non-root `appuser` uid 1000, `DOTNET_EnableDiagnostics=0`, port 8080. Solid baseline; hardening remains (G15). `.dockerignore` exists. |
| `compose.yaml` | postgres:17 (healthcheck), rabbitmq:4-management, minio + named volumes. Local dev loop works (`make infra-up` / `make api`). |
| `k8s/backend/` | deployment (runAsNonRoot, ro-rootfs, drop ALL, requests/limits, probes on `/health/{live,ready}`), hpa (CPU 70%, 1–3), pdb (minAvailable 1), service, configmap, migration-job. Style: single-line YAML maps, hand-rolled. **Keep this style for all new manifests.** |
| `k8s/postgres/` | statefulset (fsGroup 999, PVC 2Gi, pg_isready probe), headless service, secret.example. No resources/securityContext (G2). |
| `k8s/ingress/ingress.yaml` | nginx, host `forum.local`, `/api`→backend, `/`→frontend. No TLS (G13), no WS timeouts (G13), frontend Service doesn't exist yet (G4). |
| `k8s/network-policies/` | `default-deny.yaml` (ingress deny-all) only; gated off by `APPLY_NETWORK_POLICIES=false` (G1). |
| `scripts/` + `Makefile` | `lib.sh` (env, `kc`/`mk`/`compose` helpers), preflight, infra-up/down, dev-api/dev-web, setup-minikube, deploy, teardown, reset-db; Makefile wraps everything. `run-load-test.sh` passes `PROFILE` to k6 but only `smoke.js` exists (G9). `seed-test-data.sh` is a TODO stub (G10). |
| Backend observability | Serilog (console, config-driven; Loki sink package referenced but unconfigured), OTel traces (AspNetCore+HttpClient → OTLP) + metrics (AspNetCore+HttpClient+Runtime → `/metrics` Prometheus endpoint), correlation-id middleware, `/health/live` + `/health/ready` (Postgres+RabbitMQ checks, hand-rolled). |
| Load | `load/k6/smoke.js`: 5 VU × 30 s against `/health/ready` only (G9 — also an anti-pattern: load on the kubelet's readiness signal). |

### Gap register

Each gap says where it bites and which phase block fixes it.

| ID | Gap (verified) | Consequence if unfixed | Fixed in |
|---|---|---|---|
| **G1** | Only `default-deny-ingress` exists; no allow rules. Additionally **minikube's default CNI (kindnet/bridge) does not enforce NetworkPolicy at all** — the Python repo hit this and documented `--cni=calico`. | Policies are either off (today) or, if enabled, break everything; with default CNI they'd silently do nothing — a false sense of security in the thesis. | 10b |
| **G2** | `k8s/postgres/statefulset.yaml` has no resources, no container securityContext, no seccomp. | Postgres can eat the node under k6 load; fails PSS `restricted`. | 10b |
| **G3** | `k8s/rabbitmq/`, `k8s/minio/`, `k8s/frontend/`, `k8s/monitoring/` are README-only. **`scripts/deploy.sh` therefore deploys a backend whose `/health/ready` gates on RabbitMQ that is never deployed** → rollout would sit unready and `kubectl rollout status` would time out. The current mk-deploy path is broken for the Phase 6+ backend. | Cluster deploy simply does not work today. | 10b |
| **G4** | No frontend image/Dockerfile/manifests exist; ingress already routes `/`→`frontend` Service that doesn't exist. `NEXT_PUBLIC_API_URL`/`NEXT_PUBLIC_WS_URL` are baked at build time. | `/` 503s via ingress; frontend can't be deployed. | 10a (image) + 10b (manifests) |
| **G5** | `MinioObjectStorage` presigns against the single `Storage:Endpoint`. In-cluster that is `minio:9000` → **presigned PUT/GET URLs are unreachable from the browser** (and the signature binds the host, so you can't just rewrite the URL). ADR 0008's entire upload flow is dead in k8s. | No uploads/avatars/attachments work in the cluster. | 9a (code: `Storage:PublicEndpoint` + second presign client) + 10b (ingress route + CORS) |
| **G6** | Rate limiter partitions by `Connection.RemoteIpAddress`; no `UseForwardedHeaders`. Behind ingress-nginx every request arrives from the controller pod IP → **all users share one 100 req/min bucket**; the whole site 429s under trivial load. | Cluster unusable under >100 req/min total; benchmark invalid. | 9a |
| **G7** | OTel gaps: `Npgsql.OpenTelemetry` is referenced in the csproj but `AddNpgsql()` is never called (no DB spans); no EF Core instrumentation; no custom domain metrics `Meter` anywhere (verified by grep); Serilog has no JSON formatter or Loki path configured; no exemplar wiring. | Dashboards required by §7 of the requirements (domain counters, outbox lag, WS connections) impossible; traces show HTTP spans with no DB detail. | 9a |
| **G8** | Connection string sets no `Maximum Pool Size` → Npgsql default **100 per replica** (all five DbContexts share one pool — same connection string). HPA max 3 → worst case 300 connections vs Postgres 17 default `max_connections=100`. | Under scale-out: `53300 too_many_connections`, readiness flaps, benchmark collapses — exactly the failure the Python repo diagnosed. | 10b (math + config) |
| **G9** | `demo`/`stress` k6 profiles referenced by `run-load-test.sh`/Makefile but not implemented; smoke hits only `/health/ready`. | No benchmark scenarios exist; Phase 9's core deliverable missing. | 9c |
| **G10** | `seed-test-data.sh` is `echo TODO`; no seed code in the backend (only the authz role/action seed migration). | No deterministic dataset → no fair A/B comparison. | 9b |
| **G11** | `k8s/backend/configmap.yaml` points `Otlp__Endpoint` at `http://otel-collector:4317` — no such Service will exist (plan: export straight to Tempo). | Traces silently dropped in cluster. | 10b/10c |
| **G12** | No ServiceAccounts; every pod runs as `default` SA with an auto-mounted API token none of them need. No PSS labels on the namespace. | Fails least-privilege review; a compromised pod holds a (weak but real) API credential. | 10b |
| **G13** | Ingress: no TLS; `ssl-redirect: "true"` annotation with no cert (nginx redirects to a self-signed 404 experience); no `proxy-read-timeout` → **nginx kills idle WebSockets after 60 s**, forcing constant reconnect/resync churn. | Realtime feature degraded in cluster; no HTTPS story. | 10b |
| **G14** | RabbitMQ default creds are `guest/guest`, and **RabbitMQ restricts `guest` to loopback** (`loopback_users`) — an in-cluster backend connecting to `rabbitmq:5672` as guest is refused. | Backend can never become ready in cluster even after G3 is fixed. | 10b (secret + `RABBITMQ_DEFAULT_USER/PASS`) |
| **G15** | Image pipeline: only `:local` tag, no digest/git-SHA tagging, no vulnerability scanning, no BuildKit cache mounts, runtime base is full `aspnet` (shell, package manager). | No provenance for “which build produced these benchmark numbers”; slower rebuild loop; larger attack surface. | 10a |
| **G16** | Backend deployment carries `prometheus.io/scrape` pod annotations — kube-prometheus-stack **ignores annotations** by default; it discovers via ServiceMonitor/PodMonitor. | `/metrics` never scraped in cluster. | 10c (ServiceMonitor; keep annotations as documentation) |
| **G17** | No graceful-shutdown tuning: default `terminationGracePeriodSeconds` 30 with no preStop; WS connections + in-flight outbox relay claims die abruptly on rollout; rolling-update strategy left at k8s defaults (maxSurge 25%/maxUnavailable 25% → can go to 0 ready pods at replicas=1). | Rollouts during load tests produce artificial error spikes. | 10b |
| **G18** | HPA has no `behavior` block (default scale-down stabilization 300 s, scale-up unbounded) and CPU-only signal — fine, but undocumented; prometheus-adapter question unanswered. | Scaling behavior unexplained in thesis; risk of flapping. | 10b (decision recorded) |
| **G19** | `JwtOptions` silently falls back to a hard-coded dev signing key when `Jwt:SigningKey` is unset — **including in Production**. | A cluster deployed without the secret mints tokens any reader of the repo can forge. | 9a (fail-fast guard) |
| **G20** | MinIO CORS never configured; browser presigned PUT needs CORS on the S3 API when served cross-origin. | Uploads fail preflight from `http(s)://forum.local`. | 10b |
| **G21** | `/metrics`, `/health/*` are rate-limited like everything else (global limiter has no exemptions) and publicly routable via `/api` ingress path? (`/metrics` is at root — NOT under `/api`, so ingress does not expose it: verified `ingress.yaml` only routes `/api` and `/`→frontend… `/` prefix **does** match `/metrics` and routes it to the frontend, which 404s — acceptable). In-cluster Prometheus scrapes pod IP:8080 directly and **is** subject to the limiter (its own partition: ~4 req/min — fine). Documented, no change needed except the k6/bench limiter raise (9c). | Mostly latent; documented so nobody "fixes" it blindly. | 9c note |
| **G22** | `user_stats_v` karma/counts and `reaction_counts` are live-computed/trigger-fed (fine), but the feed's `like_count`/`comment_count` placeholders (`0 AS like_count`) mean the SPA does an extra Engagement batch call per feed page — deliberate Phase 4 design, **kept** (fetch-then-patch parity). Noted so the benchmark interpretation remembers A does 2 requests where B renders server-side. | Benchmark interpretation footnote. | 9c runbook note |

---

## 1. The 12 GiB resource contract

**Hard requirement:** dev machine = WSL2 on 16 GiB RAM; **at most 12 GiB to the entire minikube VM**.

**Chosen split (explicit, correct the numbers here if the machine differs):**

- `.wslconfig`: `memory=12GB`, `swap=4GB`, `processors=6` — unchanged from `docs/runbooks/wsl-minikube-setup.md`.
- **minikube VM: `MINIKUBE_MEMORY=10240` (10 GiB), `MINIKUBE_CPUS=6`** — deliberately *below* the 12 GiB
  ceiling: k6, `kubectl`, the IDE server and Docker's own overhead live in the same WSL VM and need ~2 GiB.
  Running k6 **outside** the cluster (see 9c) is part of this budget decision.
- **CPU design assumption: 6 vCPUs** for the minikube VM. This is the single number to correct if the
  host differs; VU counts in 9c and CPU limits below were sized against it.

### The one budget table (worst case = every HPA at max, all limits hit simultaneously)

Requests are what the scheduler reserves; limits are the burst ceiling. The table must (and does) satisfy:
**Σ memory limits + system overhead ≤ 10 GiB node**, so even pathological simultaneous bursts cannot OOM the VM.

| Component | ns | Replicas (max) | CPU req | CPU limit | Mem req | Mem limit | Σ Mem limit |
|---|---|---|---|---|---|---|---|
| backend (API) | forum-dotnet | **3** (HPA max) | 150m | 750m | 256Mi | 512Mi | 1536Mi |
| frontend (Next standalone) | forum-dotnet | 1 | 50m | 300m | 128Mi | 256Mi | 256Mi |
| postgres | forum-dotnet | 1 | 250m | 1000m | 512Mi | 1024Mi | 1024Mi |
| rabbitmq | forum-dotnet | 1 | 100m | 500m | 256Mi | 512Mi | 512Mi |
| minio | forum-dotnet | 1 | 100m | 500m | 256Mi | 512Mi | 512Mi |
| db-migrate / db-seed Jobs (transient) | forum-dotnet | 0–1 | 250m | 500m | 256Mi | 512Mi | (512Mi transient) |
| prometheus (kube-prometheus-stack) | monitoring | 1 | 100m | 800m | 400Mi | 768Mi | 768Mi |
| grafana | monitoring | 1 | 100m | 800m | 256Mi | 512Mi | 512Mi |
| loki (SingleBinary) | monitoring | 1 | 150m | 800m | 384Mi | 768Mi | 768Mi |
| alloy (log shipper, DaemonSet ×1 node) | monitoring | 1 | 50m | 200m | 128Mi | 256Mi | 256Mi |
| tempo (single binary) | monitoring | 1 | 100m | 500m | 256Mi | 512Mi | 512Mi |
| node-exporter | monitoring | 1 | 20m | 100m | 32Mi | 64Mi | 64Mi |
| kube-state-metrics | monitoring | 1 | 20m | 100m | 64Mi | 128Mi | 128Mi |
| prometheus-operator | monitoring | 1 | 50m | 200m | 128Mi | 256Mi | 256Mi |
| postgres-exporter | monitoring | 1 | 20m | 100m | 32Mi | 64Mi | 64Mi |
| ingress-nginx (minikube addon) | ingress-nginx | 1 | 100m | (unset by addon) | ~90Mi | ~256Mi est. | 256Mi |
| calico (CNI, required for G1) | kube-system | DS ×1 | 100m | 250m | 150Mi | 256Mi | 256Mi |
| kube-system baseline (apiserver, etcd, coredns, scheduler, controller-mgr, kubelet/containerd) | kube-system | — | — | — | — | ~1200Mi est. | 1200Mi |
| *(optional, default OFF — see 10d)* redis | forum-dotnet | (1) | (50m) | (200m) | (64Mi) | (128Mi) | (128Mi) |
| **TOTAL (worst case, without optional redis, + transient job)** | | | **~1.7 cores req** | | **~4.3Gi req** | | **≈ 8.9–9.4 GiB** |

**UPDATE (2026-07-10):** Benchmark dataset size revised downward (AMENDMENT A1): Benchmark profile now targets
750–1000 users / 1500–2000 threads / 8000–12000 comments (vs original 2000 / 10k / 60k). PostgreSQL footprint
drops to ~200–400 MB, freeing headroom for Loki/Tempo logging surge under k6 at 150 VU. Keyset pagination,
FTS, and trigger logic are **not** a function of dataset size — they're validated in Phase 2–4 tests; this
reduction prioritizes **fair benchmark conditions on the target hardware** (WSL2 Minikube 10 GiB) over
"production scale" which belongs in a separate capacity study.
> **MEASURED (2026-07-11, Phase 9b implemented):** the LOCKED Benchmark seed (800 users / 1600 threads /
> 9000 comments / 15000 reactions) is **24 MB** on disk — an order of magnitude under this ~200–400 MB estimate,
> leaving even more headroom than budgeted. Postgres request/limit (512Mi/1Gi) stays as-is.

**Verdict: fits comfortably.** ≈8.5–9.0 GiB worst case on a 10 GiB node with reduced-scale Benchmark seed.
Headroom exists *because scope was cut deliberately*:

- **Alertmanager disabled** (rules still evaluate and display in Prometheus/Grafana — the Python repo made
  the same call; there is no pager to notify on a thesis laptop). Saves ~128Mi.
- **Prometheus retention 6h / 2GiB disk cap** (a benchmark session is reviewed same-day; export snapshots
  to `thesis/` instead of retaining).
- **Loki: 24 h retention, filesystem storage, ingestion caps** (8 MB/s — k6 runs flood logs; the Python repo
  OOM'd Loki without this).
- **Tempo: 24 h retention, filesystem**, head-based sampling stays at 100% only because request volume is
  benchmark-scale, revisit if trace RAM grows.
- **k6 runs OUTSIDE the cluster** (WSL host) — the Python repo ran it as an in-cluster Job; we deviate to
  keep the cluster budget for the system under test and to measure the full ingress path (documented in 9c).
- CPU limits over-subscribe 6 vCPUs (~7.2 cores of limits) — normal and fine; requests (~1.7) are what
  matters for scheduling. Grafana's 800m CPU limit is a direct lesson from the Python repo (CFS throttling
  at 300m caused liveness-probe restarts during k6 runs).

If it ever does NOT fit (e.g. host only has 8 GiB for minikube): drop backend HPA max to 2 (−512Mi),
Prometheus retention to 3h/768Mi limit, Tempo to 384Mi, and skip calico + NetworkPolicies (documented
trade-off) — in that order.

---

## 2. Target topology

```
WSL2 (12 GiB) ─ k6 (host) ──► http(s)://forum.local ─┐
                                                      ▼
minikube VM (10 GiB, 6 vCPU, --cni=calico)   [ingress-nginx  ns:ingress-nginx]
                                                │ /api,/api/realtime/ws → backend:80
                                                │ /            → frontend:80
                                                │ minio.forum.local → minio:9000  (presigned)
                                                │ grafana.forum.local → grafana   (ns:monitoring)
  ns: forum-dotnet  (PSS: restricted)           ▼
    backend ×1..3 (HPA) ── postgres:5432 (StatefulSet, PVC)
        │  │  └───────────── rabbitmq:5672 (StatefulSet, PVC; prometheus :15692)
        │  └──────────────── minio:9000  (StatefulSet, PVC; metrics :9000)
        └ /metrics :8080 ◄── scraped by Prometheus (ServiceMonitor)
    frontend ×1 (Next.js standalone, CSR shell only)
    Jobs: db-migrate → db-seed (both: image forum-dotnet-api, args migrate|seed)
  ns: monitoring  (PSS: baseline — node-exporter/alloy need host access)
    kube-prometheus-stack (prometheus, grafana, operator, node-exporter, kube-state-metrics)
    loki + alloy (logs), tempo (OTLP :4317 ← backend traces), postgres-exporter
  NetworkPolicies: default-deny ingress in forum-dotnet + explicit allows (G1)
```

DNS names used throughout: `postgres`, `rabbitmq`, `minio`, `backend`, `frontend` (ns `forum-dotnet`);
`tempo.monitoring.svc.cluster.local:4317`, `loki.monitoring.svc.cluster.local:3100`.

---

## Phase 9a — Backend observability finalization (code)

**Goal.** Close every backend-code gap the cluster and dashboards depend on: domain metrics Meter, DB
tracing, JSON logs with trace correlation, forwarded-headers correctness, the MinIO public presign endpoint,
and the Production JWT fail-fast. After this phase the image is *cluster-ready*; 10b only adds YAML.

**Depends on.** Phase 8 complete (it is). Nothing in k8s.

**Steps.**

1. **Domain metrics Meter (G7).** New file `backend/src/Shared/Forum.Common/Telemetry/ForumMetrics.cs` —
   `System.Diagnostics.Metrics` is BCL, so `Forum.Common` needs **no new package** and modules can inject it
   without referencing OpenTelemetry (Domain-purity untouched; this is Application/Infrastructure-level DI):

   ```csharp
   using System.Diagnostics.Metrics;

   namespace Forum.Common.Telemetry;

   /// <summary>Domain-level counters/gauges required by REQUIREMENTS §7. Registered as a singleton;
   /// exported because ObservabilityExtensions calls AddMeter(MeterName).</summary>
   public sealed class ForumMetrics
   {
       public const string MeterName = "Forum";
       private readonly Meter _meter;

       public ForumMetrics(IMeterFactory factory)
       {
           _meter = factory.Create(MeterName);
           AuthAttempts     = _meter.CreateCounter<long>("forum.auth.attempts", description: "Login attempts by outcome");
           ThreadsCreated   = _meter.CreateCounter<long>("forum.threads.created");
           CommentsCreated  = _meter.CreateCounter<long>("forum.comments.created");
           Reactions        = _meter.CreateCounter<long>("forum.reactions", description: "Reaction toggles by action");
           OutboxPublished  = _meter.CreateCounter<long>("forum.outbox.published", description: "Relay publishes by module");
           OutboxFailures   = _meter.CreateCounter<long>("forum.outbox.publish_failures");
           OutboxLag        = _meter.CreateHistogram<double>("forum.outbox.lag", unit: "s",
                                  description: "OccurredOn → broker-confirm latency");
           MessagesConsumed = _meter.CreateCounter<long>("forum.messaging.consumed", description: "By module + outcome (ok|retry|poison|duplicate)");
           WsConnections    = _meter.CreateUpDownCounter<long>("forum.ws.connections");
           WsSubscriptions  = _meter.CreateUpDownCounter<long>("forum.ws.subscriptions");
           WsPushes         = _meter.CreateCounter<long>("forum.ws.pushes");
       }
       // expose the instruments as get-only properties …
   }
   ```

   Wire-up points (each is a 1–2 line `Add`/tag call, keep handlers thin):
   - `LoginUser` handler (Identity): `AuthAttempts.Add(1, new("outcome", "success"|"invalid_credentials"|"blocked"))`.
     **Never** tag with the email/username — cardinality and privacy.
   - `CreateThread` / `CreateComment` handlers (Content): increment on success only.
   - `AddReaction`/`RemoveReaction` (Engagement): `Reactions.Add(1, new("action","add"|"remove"))` — only on
     actual state change, not on idempotent no-ops (tag `("noop","true")` is tempting; skip it, keep cardinality 2).
   - `OutboxRelayService<TContext>` (Forum.Infrastructure): on broker confirm →
     `OutboxPublished.Add(1, new("module", moduleName))` + `OutboxLag.Record((now - message.OccurredOnUtc).TotalSeconds)`;
     on failure → `OutboxFailures.Add(1, new("module", moduleName))`.
   - `IntegrationEventConsumerService<TContext>`: `MessagesConsumed.Add(1, new("module", m), new("outcome", o))`.
   - Realtime hub (`Forum.Api/Realtime/`): connection open/close → `WsConnections.Add(±1)`; subscribe/unsubscribe →
     `WsSubscriptions.Add(±1)`; every notification actually written to a socket → `WsPushes.Add(1)`.
   - Register in `AddForumInfrastructure` (or `Forum.Common` DI helper): `services.AddSingleton<ForumMetrics>();`.

   Prometheus names after export (the exporter translates `.`→`_`, appends `_total` to counters):
   `forum_auth_attempts_total`, `forum_threads_created_total`, `forum_comments_created_total`,
   `forum_reactions_total`, `forum_outbox_published_total`, `forum_outbox_publish_failures_total`,
   `forum_outbox_lag_seconds_bucket`, `forum_messaging_consumed_total`, `forum_ws_connections`,
   `forum_ws_subscriptions`, `forum_ws_pushes_total`. These exact names are used in the 10c dashboards.

2. **Tracing completion (G7).** `ObservabilityExtensions.AddForumObservability`:
   - `.WithTracing(t => t.AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health") && !ctx.Request.Path.StartsWithSegments("/metrics")) .AddHttpClientInstrumentation() .AddNpgsql() .AddOtlpExporter())`
     — the `Npgsql.OpenTelemetry` package is *already referenced*; it was simply never wired. Health/metrics
     filtering keeps Tempo from drowning in probe spans (2 probes × 3 pods × every 10 s).
   - Add `OpenTelemetry.Instrumentation.EntityFrameworkCore` to `Directory.Packages.props` (CPM) + csproj, and
     `.AddEntityFrameworkCoreInstrumentation(o => o.SetDbStatementForText = true)` — gives per-query spans with
     SQL text for the writes path (reads are raw ADO → covered by Npgsql instrumentation).
   - `.WithMetrics(m => m.AddMeter(ForumMetrics.MeterName) …)` — without this the Meter exists but exports nothing.
   - `ConfigureResource`: add `resource.AddAttributes([new("deployment.environment", builder.Environment.EnvironmentName)])`.

3. **Structured JSON logs + trace correlation (G7).** Create `appsettings.Production.json` (new file, safe to
   commit — no secrets):

   ```json
   {
     "Serilog": {
       "MinimumLevel": { "Default": "Information", "Override": { "Microsoft.AspNetCore": "Warning" } },
       "WriteTo": [ { "Name": "Console", "Args": { "formatter": "Serilog.Formatting.Compact.CompactJsonFormatter, Serilog.Formatting.Compact" } } ],
       "Enrich": [ "FromLogContext" ]
     }
   }
   ```

   Decision — **logs go to stdout as compact JSON and are shipped by Alloy** (10c), NOT pushed by the app via
   `Serilog.Sinks.Grafana.Loki`. Rationale: 12-factor, zero app-side buffering/backpressure concerns during k6
   floods, Kubernetes metadata labels for free, and one fewer failure mode inside the measured system. The Loki
   sink package may stay referenced (harmless) or be dropped from the csproj — dropping is cleaner; do it.
   Serilog ≥3.1 captures `TraceId`/`SpanId` from `Activity.Current` natively into CompactJsonFormatter output —
   that is the field Grafana's derived-field → Tempo link uses (10c). Correlation-id is already enriched by the
   existing middleware; verify the property name (`CorrelationId`) appears in JSON output and note it for LogQL.

4. **Forwarded headers (G6).** In `Program.cs`, **before** `UseSerilogRequestLogging`/rate limiting:

   ```csharp
   builder.Services.Configure<ForwardedHeadersOptions>(options =>
   {
       options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
       // Trust exactly the pod network (ingress-nginx lives there); never trust by default in Production.
       options.KnownNetworks.Add(new IPNetwork(IPAddress.Parse("10.244.0.0"), 16));
   });
   …
   app.UseForwardedHeaders();   // first middleware, before anything reads RemoteIpAddress
   ```

   Make the CIDR configurable (`ForwardedHeaders:KnownNetworks` array) with `10.244.0.0/16` as the k8s default
   (both kindnet and calico on minikube use it; verify with `kubectl get pods -o wide` after 10b and correct the
   ConfigMap if different). Add a unit/integration test: request with spoofed `X-Forwarded-For` from an
   untrusted source keeps the socket IP; from a trusted proxy adopts the header.

5. **MinIO public presign endpoint (G5).** `StorageOptions` gains
   `public string? PublicEndpoint { get; init; }` (+ `public bool PublicUseSsl { get; init; }`) — when set,
   `MinioObjectStorage` builds a **second** `IMinioClient` with the public endpoint and uses it **only** for
   `PresignPutAsync`/`PresignGetAsync` (signatures bind the host, so the signing client must be configured
   with the URL the browser will hit). All server-side ops (Stat/ReadRange/Remove/EnsureBucket) keep the
   internal client. Default `null` → single-client behavior, so compose/dev is untouched. Unit test: presigned
   URL host == PublicEndpoint when set. Cluster value (10b): `minio.forum.local` via ingress.

6. **Production JWT fail-fast (G19).** In `AuthenticationExtensions` (where `JwtOptions` binds): if
   `environment.IsProduction() && string.IsNullOrWhiteSpace(options.SigningKey)` → throw
   `InvalidOperationException("Jwt:SigningKey must be provided in Production (k8s Secret).")` at startup.
   The dev fallback stays for Development/tests only. Integration test with `ASPNETCORE_ENVIRONMENT=Production`
   asserting the host refuses to boot.

7. **Graceful-shutdown groundwork (G17, code half).** `builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(25));`
   (k8s `terminationGracePeriodSeconds: 40` in 10b > 25 s drain + preStop 5 s). Verify the hosted services
   (relay, consumer host, sweeper, realtime feed) all honor cancellation promptly — they do (BackgroundService
   pattern); note WS sockets get closed by host shutdown, clients resync on reconnect by design (ADR 0010).

8. **Verification.** `dotnet build` · `dotnet format --verify-no-changes` · `dotnet test` all green; run
   locally against compose with `curl localhost:8080/metrics | grep forum_` showing the new series after
   exercising login/thread/comment/reaction/upload paths; a trace in the console OTLP debug (or against a
   local Tempo via `infrastructure/local/`) shows HTTP → Npgsql spans.

**Watch out.**
- Metric tag cardinality: outcome/action/module tags only — never user ids, emails, category ids.
- `AddMeter(ForumMetrics.MeterName)` — forgetting it is the classic silent-nothing failure.
- `UseForwardedHeaders` MUST be first in the pipeline or correlation/rate-limit/serilog see the proxy IP.
- Don't move `/metrics` off the app port; ServiceMonitor scrapes the pod directly (10c) — no ingress exposure.
- The presign change touches `IObjectStorage`'s implementation only — the interface is unchanged; Files module
  code must not need edits.
- ArchitectureTests: `ForumMetrics` lives in `Forum.Common` (shared), injected into module Application layers —
  same pattern as `ICorrelationContext`; Domain stays free of it.

**Definition of Done.** All Phase 8 tests + new tests green; `/metrics` exposes every `forum_*` series listed
above; Npgsql + EF spans visible; Production boot without `Jwt:SigningKey` fails with the explicit message;
presigned URL host switches with `Storage:PublicEndpoint`; JSON console logs in Production mode contain
`TraceId` + `CorrelationId`.

**START-OF-PHASE REMINDERS.**
- *Remember:* one Meter named `Forum` in `Forum.Common`, BCL-only, injected like `ICorrelationContext`;
  wire `AddMeter` + `AddNpgsql` or nothing exports; logs = compact JSON to stdout (Alloy ships them — do NOT
  add the Loki sink); `UseForwardedHeaders` first; presign via second client, interface unchanged; Production
  refuses to boot without a real JWT key. Exact metric names here are load-bearing — 10c dashboards use them.

---

## Phase 9b — Deterministic seed

**Goal.** Implement **two named seed profiles** (Development + Benchmark) — both deterministic and reproducible,
enabling rapid local dev and fair benchmark comparison with Architecture B. (See AMENDMENTS section A1–A3 above
for rationale and database isolation strategy.)

**Depends on.** 9a not required (independent), but do it after 9a so the seeded image is the final one.

> **IMPLEMENTED — Phase 9b code-complete + verified (2026-07-11).** Locked numbers and corrections below
> supersede the earlier ranges/sketches in this block (kept for context). Everything was verified against a
> real Postgres, not assumed.
>
> - **Wiring (corrects the Makefile/compose sketches, which guessed a containerized `api`):** seeding mirrors
>   the `migrate` pattern exactly — `Program.cs` gains a `seed` arg branch → `SeedRunner.RunSeedAsync(SeedConfig)`
>   (new extension in `Forum.Infrastructure/Startup`, parallel to `MigrationRunner`, early `return`, never on a
>   normal boot). Per-module `IModuleSeeder`s (`IdentitySeeder`→`ContentSeeder`→`EngagementSeeder`, `int Order`)
>   are registered by each module installer and resolved by `SeedRunner` — **not** as `IStartupTask`s (those would
>   fire every boot). Shared determinism lives in `Forum.Infrastructure/Seeding/` (`SeedProfile`, `SeedConfig`,
>   `SeedPlan`, `SeedStreams`, `SeedTime`, `SeedUlids`, `SeedDistribution`, `IModuleSeeder`).
> - **Determinism mechanic (corrects the plan's `Ulid.NewUlid(baseTime + i*offset)`):** that overload draws its
>   random bits from a crypto RNG and is NOT reproducible. Ids come from `Ulid.NewUlid(SeedTime.At(stream,i),
>   SHA256(seed:stream:i)[..10])` — a pure function of (stream, index), so any module seeder reconstructs a
>   cross-module reference (owner, target) from stream+index alone, no shared RNG state, no project reference.
>   The embedded timestamp equals `created_on_utc`, so ids sort by creation. Verified: identical ULIDs across
>   two fresh runs, per profile (unit test + integration `SeedFlowTests` + manual md5 of both profiles' id sets).
> - **Audit at seed time:** `AuditInterceptor` now skips stamping when `CreatedOnUtc` is already set (a
>   freshly-constructed aggregate is always `default`, so request-path inserts are unaffected). Seeders set
>   deterministic `created_on_utc`; `created_by` is null on users (matches anonymous self-registration) and the
>   owner on content. New internal `Seed(…)` factories on `User/Category/Thread/Comment/Tag` build aggregates
>   with an explicit id + audit and **raise no events**; seeders call plain `SaveChangesAsync` (not the
>   dispatch variant) → **zero outbox rows** (verified). `search_tsv` and `reaction_counts` are filled by their
>   row triggers on every insert — verified consistent (FTS corpus hits; zero counter drift).
> - **Private-category "membership" = a `moderate` ACL at category scope** (bit 6 = 64): the code's private
>   gate is *owner-or-moderate*, so that is the only grant that opens a private category. IdentitySeeder writes
>   these into `forum_authz` (its own schema) using deterministically-reconstructed category ids — no Content
>   reference. Role grants + ACLs are one bulk `unnest` INSERT each, then one bulk `recompute_user_perms`.
> - **Files seeding deliberately omitted** (plan listed 5–10 Dev PNGs): a `files` row with `status='committed'`
>   pointing at a MinIO object that was never uploaded would break the presigned GET/download path, and the seed
>   CLI intentionally needs only Postgres (not MinIO). Avatars/icons stay null; the SPA already renders null
>   avatars. Benchmark seeds 0 files by design anyway (uploads are a k6 scenario in 9c).
> - **DB isolation:** `compose.yaml` already defaults `${POSTGRES_DB:-forum_net}` (unchanged). Locally,
>   `scripts/seed-test-data.sh` (rewritten from the TODO stub) + `make seed` target `forum_net`; `--benchmark`
>   `CREATE DATABASE forum_net_bench` idempotently on the *same* server (via `lib.sh ensure_database`) and seed
>   there with `--force` — both datasets coexist, no volume wipe. In-cluster the two Jobs share one DB (the
>   secret's connection string); the profile arg controls volume (plan §A2).
> - **LOCKED Benchmark numbers + MEASURED size:** users **800** (2 admin / 10 moderator / 20 blocked),
>   categories **12** (4 private × 25 member ACLs), tags **60**, threads **1600** (1% pinned, 1% soft-deleted),
>   comments **9000** (depth 0–4, ≤1% deleted, longest path 134 ≤ 161 chars), reactions **15000** (Zipf, 75%
>   thread / 25% comment). **Real `pg_database_size` = 24 MB** (threads 5.0 MB / comments 4.6 MB / reactions
>   3.9 MB incl. indexes+tsvector) — the plan's 200–400 MB estimate was ~15× high; 24 MB sits far under the 1 GiB
>   Postgres container limit (§1), so the numbers are kept as-is (safe, not reduced). Seed time ≈ 13 s.
> - **How a developer runs it:** `make seed` (Development → `forum_net`, aborts if already seeded) ·
>   `make seed ARGS=--benchmark` (Benchmark → `forum_net_bench`, `--force` reset) · add `ARGS=--cluster` for the
>   k8s Job (`k8s/backend/seed-job.yaml` / `seed-job-benchmark.yaml`). Tests stay Testcontainers-isolated (A3).

### Data volume profiles (user-suggested scalings from 2026-07-10; coordinate final Benchmark numbers with Fable 5)

**Development Profile** — fast dev loop (`make api`):

| Entity | Count | Rules |
|---|---|---|
| users | 5 | `admin@dev.local`, `mod@dev.local`, `alice@dev.local`, `bob@dev.local`, `charlie@dev.local`; ONE precomputed Argon2id hash (literal `Dev#Password1`) reused; usernames lowercase; 1 global moderator, 1 admin |
| categories | 2 | `general` (public), `private-club` (private); both owned by admin |
| tags | 4 | `tag1`, `tag2`, `tag3`, `tag4` |
| threads | 10 | Evenly distributed; titles `Dev thread {i:D2}`, simple bodies (<500 chars each); 0 pinned; 0 deleted |
| comments | 10 | Total across all threads; 0 nested (or 1–2 at depth 1 for demo); 0 deleted |
| reactions | 0–5 | Optional light reactions for UI demo |
| files | 5–10 | Placeholder PNGs (1×1 px): avatars (2), category icons (2), inline images (2–3) — self-contained within test run |

**Seed time: <5 seconds** (no Argon2 scaling, no Zipf, tiny payload). **DB size: ~10–20 MB**.

**Benchmark Profile** — thesis comparison (`make bench-local` / k8s measured runs):

| Entity | Count | Rules |
|---|---|---|
| users | 750–1000* | `bench_user_0001@bench.local`…; ONE precomputed Argon2id hash (literal `Bench#Password1`); global roles: 10 moderators, 2 admins; 20 blocked |
| categories | 12–15 | Mix public/private; slugs `bench-cat-01`…; owners round-robin; private ones get 20–30 member ACL entries |
| threads | 1500–2000 | Zipf-ish spread (top 3 categories get 50% of threads); deterministic titles + bodies 0.5–2 KiB markdown; 1% pinned; 1% soft-deleted |
| comments | 8000–12000 | Depth distribution 60/25/10/4/1% for depths 1–5 (keyset pagination + recursion challenges); 1–2% soft-deleted |
| tags / thread_tags | 50–100 / 5000–8000 | Tags `tag-001`…; deterministic 0–5 per thread |
| reactions | 10000–20000 | Zipf over hot threads (counter trigger stress-test) |
| files | 0 | Presigned-upload tested separately in k6 scenario (9c) |

**\* Benchmark user count rationale:** k6 setup() logs in a pool of 200 from N users anyway. The remaining 550–800
users populate the "crowd" (created threads/comments as context). 750–1000 balances realism (enough variety for
Zipf distribution) and memory constraints (Minikube 10 GiB, §1).

**Seed time: 30–60 seconds** (Argon2 hashing at seeder start, batch INSERTs). **DB size: 200–400 MB**.
> *Measured 2026-07-11 with the LOCKED numbers (800/12/60/1600/9000/15000): actual **`pg_database_size` = 24 MB**,
> seed ≈ **13 s**. The 200–400 MB estimate above was ~15× high; the numbers are kept (far under the 1 GiB budget).*

**Both profiles deterministic:** Fixed RNG seed (`Random(20260707)`) + fixed timestamp base + ULID
generation (`Ulid.NewUlid(baseTime + i*offset)`) → identical IDs and keyset order across runs on fresh DBs.

**Steps.**

1. **Seed configuration types.** New files in `backend/src/Shared/Forum.Infrastructure/Seeding/`:

   ```csharp
   // SeedProfile.cs
   namespace Forum.Infrastructure.Seeding;
   
   public enum SeedProfile
   {
       Development,  // Fast local dev (5 users, 2 categories, ~10 MB)
       Benchmark     // Thesis runs (750–1000 users, deterministic, ~300 MB)
   }
   
   // SeedConfig.cs
   public record SeedConfig(
       SeedProfile Profile,
       bool AllowTruncate = false,
       bool Verbose = false
   );
   ```

2. **Entry point.** Mirror the `migrate` pattern: `Program.cs` gains
   ```csharp
   if (args.Contains("seed"))
   {
       var profile = args.Contains("--benchmark") ? SeedProfile.Benchmark : SeedProfile.Development;
       var allowTruncate = args.Contains("--force");
       var config = new SeedConfig(profile, allowTruncate);
       await app.RunSeedAsync(config);
       return;
   }
   ```

   New `backend/src/Shared/Forum.Infrastructure/Startup/SeedRunner.cs` resolves all registered `IModuleSeeder`s
   (new interface next to `IStartupTask`: `int Order`, `Task SeedAsync(SeedConfig, CancellationToken)`) and
   runs them in module-registration order (Identity → Content → Files (no-op) → Engagement) inside a stopwatch
   + row-count log summary + profile name in output.
2. **Per-module seeders** live in each module's `Infrastructure/Seeding/` (internal, registered by the module
   installer): `IdentitySeeder`, `ContentSeeder`, `EngagementSeeder`. **They accept `SeedConfig` and branch
   on `config.Profile` to set entity counts.** They write through the module's own DbContext with plain
   `AddRange` batches (1 000 rows / `SaveChangesAsync`), building entities directly — NOT through use-case
   handlers (100× faster, no permission churn) — and **must not raise domain or integration events** (12k
   Benchmark comments → 12k outbox rows → RabbitMQ load; construct entities via a seeding path that skips
   `Raise`, or clear events before save). The audit interceptor stamps a fixed system actor (`ICurrentActor`
   null → seeder sets explicit `created_by = SystemUser` etc.; verify interceptor tolerates pre-set values).

3. **Determinism.** **Single `Random(20260707)` seed for BOTH profiles** — the profile switch only affects
   *count*, not seeding logic, so ordering is deterministic for all users. ULIDs from
   `Ulid.NewUlid(fixedTimestampBase.AddSeconds(i))` so IDs — and therefore keyset order — are identical
   across machines/runs. No `DateTime.UtcNow` anywhere in seed data; timestamps spread deterministically over
   a 30-day window (Development uses shorter range, both end at `2026-07-01T00:00:00Z`).
4. **Database isolation (AMENDMENT A2).** Modify `compose.yaml`:
   ```yaml
   services:
     postgres:
       environment:
         POSTGRES_DB: ${POSTGRES_DB:-forum_net}  # default = dev, override in Makefile
   ```
   
   `scripts/lib.sh` gains helpers:
   ```bash
   compose_dev() {   POSTGRES_DB=forum_net docker compose "$@"; }
   compose_bench() { POSTGRES_DB=forum_net_bench docker compose "$@"; }
   ```

   Makefile targets (new/updated):
   ```makefile
   api:            ## Start dev API (Development seed, forum_net)
       $(COMPOSE_DEV) down -v && \
       $(COMPOSE_DEV) up -d && \
       $(DOCKER_EXEC_DEV) api dotnet run -- seed && \
       $(DOCKER_EXEC_DEV) api dotnet run --project src/Bootstrap/Forum.Api

   bench-local:    ## Benchmark locally (Benchmark seed, forum_net_bench)
       $(COMPOSE_BENCH) down -v && \
       $(COMPOSE_BENCH) up -d && \
       $(DOCKER_EXEC_BENCH) api dotnet run -- seed --benchmark --force && \
       $(DOCKER_EXEC_BENCH) api k6 run load/k6/main.js -e PROFILE=stress
   ```

   **Result:** `forum_net` and `forum_net_bench` are separate PostgreSQL databases on the same host.
   Development seed runs in one, Benchmark in the other. No cross-pollution.

5. **Idempotency guard.** Seeder aborts with a clear message if `users` count > profile-specific sentinel
   (e.g., Development: if `users` > 10, benchmark: if `users` > 2000) unless `--force` is passed, which
   TRUNCATEs the module's tables (schema-scoped, CASCADE) first. The k8s Job never passes `--force`
   (fail-fast on non-empty); local `make bench-local` always passes it (safe reset).
6. **Cross-module consistency.** Content's views JOIN `forum_identity.users` (view-level read join) — seeder
   order guarantees users exist first. `reaction_counts` is trigger-maintained → correct automatically.
   `search_tsv` trigger fires on INSERT → FTS correct automatically. **Verify both after seeding** in the
   integration test (`reaction_counts` row for a hot thread equals the seeded count; FTS query for a corpus
   word returns hits).

7. **k8s Jobs.** Two variants in `k8s/backend/`:
   - `seed-job.yaml`: `args: ["seed"]` (Development, for manual exploration / rollout)
   - `seed-job-benchmark.yaml`: `args: ["seed", "--benchmark", "--force"]` (for measured runs)
   
   Both share the same secret/config wiring, `backoffLimit: 0` (failed seed must not retry). Deployment
   procedure (10b) decides which to apply; `bench-run.sh` (9c) applies the Benchmark variant.

8. **Scripts.** Rewrite `scripts/seed-test-data.sh`:
   ```bash
   #!/bin/bash
   # Usage: seed-test-data.sh [development|benchmark] [--cluster]
   
   PROFILE="${1:-development}"
   CLUSTER="${2:---local}"
   
   if [[ "$CLUSTER" == "--local" ]]; then
       # Local: use Makefile wrapper
       case "$PROFILE" in
           development) make api ;;
           benchmark)   make bench-local ;;
           *) echo "Unknown profile: $PROFILE"; exit 1 ;;
       esac
   else  # --cluster
       # Cluster: apply the appropriate k8s Job
       case "$PROFILE" in
           development)
               kubectl apply -f k8s/backend/seed-job.yaml
               ;;
           benchmark)
               kubectl apply -f k8s/backend/seed-job-benchmark.yaml
               ;;
       esac
       kubectl wait --for=condition=complete job/db-seed --timeout=600s
       # Print row counts
       kubectl exec -it postgres-0 -- psql -U forum -c \
           "SELECT 'users' as table, count(*) FROM forum_identity.users UNION ALL \
            SELECT 'categories', count(*) FROM forum_content.categories UNION ALL \
            SELECT 'threads', count(*) FROM forum_content.threads UNION ALL \
            SELECT 'comments', count(*) FROM forum_content.comments;"
   fi
   ```

   Makefile: `seed: ## Seed database (ARGS=development|benchmark CLUSTER=--local|--cluster)`.

9. **Test isolation (AMENDMENT A3).** Integration tests remain **completely isolated** via Testcontainers:
   ```csharp
   // ForumApiFactory.cs (unchanged)
   public class ForumApiFactory : WebApplicationFactory<Program>
   {
       private readonly PostgresFixture _db = new();
       
       protected override void ConfigureWebHost(IWebHostBuilder builder)
       {
           // Each test session: fresh Testcontainers Postgres, zero interference
           builder.ConfigureServices(services =>
               services.AddScoped(_ => _db.CreateDbContext())
           );
       }
   }
   ```
   
   Tests seed themselves (micro-seeds, 2–10 rows per test) and never read from Development or Benchmark DBs.
   Running `make test` while `make api` is live doesn't interfere.

10. **Tests.** `Modules.*.Tests` unit tests for the deterministic generators (same seed → same first/last ULID,
    for BOTH profiles); one `IntegrationTests` case running the **Development** profile against Testcontainers
    (fast — this is what CI would run) and asserting counts + the two §6 consistency checks + a second run
    aborts (idempotency guard). The Benchmark profile is exercised manually via `make bench-local` /
    `make bench` (9c), not in the automated suite — it's too slow/heavy for `dotnet test` on every run.

**Watch out.**
- **No outbox writes during seed** — this is the difference between a fast seed and a broker meltdown.
- Argon2id: ONE hash computed, reused per profile — hashing 750–1000 real passwords individually would add
  real minutes of CPU (Argon2 is deliberately slow); reuse is not a shortcut, it's the point.
- Comment `path` must satisfy the ≤161-char/depth-5 constraints — build paths exactly like `Comment.CreateReply`.
- Keep per-batch `SaveChanges` + `ChangeTracker.Clear()` or EF tracking makes the Benchmark-profile batches
  (thousands of comments) degrade toward O(n²).
- The seeder must produce the SAME dataset as B's seeder *in shape and volume* — the exact text corpus need
  not match B, but counts, depth distribution, and hot/cold skew MUST (fairness). Record the final agreed
  Benchmark numbers in this file when locked with B (the Development profile is A-internal only, no parity
  requirement).

**Definition of Done.** `make api` on a fresh compose DB completes <5 min (Development seed), is browsable;
`make bench-local` completes <2 min (Benchmark seed, parallel setup), ready for k6; both abort safely when run
twice without `--force`; `scripts/seed-test-data.sh development --local` and `--cluster` both work;
`dotnet test` (Testcontainers) passes regardless of local DB state; row counts match the profile tables above;
FTS + `reaction_counts` verified; determinism test green (seeding identical profiles on fresh DBs produces
identical ULIDs).

**START-OF-PHASE REMINDERS.**
- *Remember:* **Two profiles (Development vs Benchmark), ONE seeder implementation** — branch on
  `config.Profile` to set counts only, seeding logic is shared. Seed writes entities directly per module
  (order Identity→Content→Engagement), batched, **zero events/outbox rows**, ONE precomputed Argon2id hash
  per profile, fixed RNG seed + fixed timestamp base for reproducible ULIDs/keysets. Triggers give you
  `search_tsv` + `reaction_counts` for free — verify, don't recompute. **Database isolation:** compose uses
  `${POSTGRES_DB:-forum_net}`, `make api` sets Development, `make bench-local` sets Benchmark. Job
  variants: `seed-job.yaml` (Development, no-force) and `seed-job-benchmark.yaml` (Benchmark, --force).
  **Tests remain fully isolated** via Testcontainers; they never consume Development/Benchmark seeds.
  Guard against seeding a non-empty DB (abort unless `--force`). If Fable finds better numbers/approach,
  update the constants in SeedProfile definitions — the framework is flexible.

---

## Phase 9c — k6 load profiles + benchmark runbook

**Goal.** Implement the missing `demo` and `stress` profiles as *realistic golden-path traffic* (not
health-check pings), plus the repeatable measurement procedure that produces the thesis numbers. (Note:
operates against the reduced Benchmark dataset from 9b — see AMENDMENTS A1 for scaling rationale.)

**Depends on.** 9b (seeded Benchmark data), 10b+10c for the *measured* runs (cluster + dashboards). Script
development itself can run against local compose (`make bench-local` prepares the isolated `forum_net_bench`
database).

**Design decisions (explicit):**
- **k6 runs on the WSL host, outside the cluster**, targeting `http://forum.local` through ingress-nginx.
  Deviation from the Python repo (in-cluster Job) — justification: (a) the cluster RAM budget is reserved for
  the system under test; (b) the measured path then includes ingress, which is what a real user hits and what
  B will also be measured through; (c) no NetworkPolicy holes for a load pod. Cost: k6's ~0.5–1 GiB RAM at
  150 VU comes out of the ~2 GiB WSL slack — sized below to fit. **CPU assumption: 6 vCPUs shared** (stated
  in §1); if k6 itself saturates CPU, reduce VUs before trusting latency numbers (k6 warns via `k6_...` own metrics).
- **File layout:** `load/k6/main.js` (scenarios + profiles + weighted mix) importing `load/k6/lib/api.js`
  (auth/session helpers) and `load/k6/lib/assets.js` (embedded 1×1 PNG bytes). Keep `smoke.js` as a two-line
  wrapper re-exporting `main.js` with `PROFILE=smoke` default? No — **delete `smoke.js`**, `run-load-test.sh`
  already passes `PROFILE`; point it at `main.js` (one source of truth).
- **Rate limiter:** measured runs REQUIRE `RateLimiting__Global__PermitLimit` ≫ default (100/min/IP would 429
  the whole test from one host IP — G21). `bench-run.sh` (below) sets it to `1000000` for the run and restores
  after, and records that fact into the run's metadata JSON. Auth endpoints keep their tighter limit EXCEPT
  the login storm in setup — stagger logins (see below) instead of raising `Auth` (parity: B has no equivalent
  limiter knob; document in thesis).

**Profiles** (plateaus ≥ 90 s so the HPA — 15 s metrics + stabilization — visibly steps; numbers sized for
6 vCPU / backend limits from §1):

| Profile | Stages | Purpose / thresholds |
|---|---|---|
| `smoke` | 5 VU × 60 s constant | CI/sanity. `http_req_failed<1%`, `p(95)<500ms` |
| `demo` | 0→10 (1m) → 40 (30s ramp + 2m plateau) → 80 (30s ramp + 2m plateau) → 0 (1m) ≈ 7 min | The HPA showcase: 1→2→3 replicas on the Grafana HPA panel; `p(95)<800ms`, errors<2% |
| `stress` | 0→50 (30s+1m) → 100 (30s+1m) → 150 (30s+2m peak) → 0 (1m) ≈ 6.5 min | Find the knee: pool saturation, queue growth; thresholds informational (`p(95)<2000ms`, errors<5%) — stress documents limits, it must not abort |

Additionally a parallel **`ws` scenario** (both demo+stress): 20 VUs (demo) / 40 (stress), each opens the
realtime socket via `POST /api/realtime/ticket` → `ws://forum.local/api/realtime/ws?ticket=…`, subscribes to a
random seeded category, holds for the test duration counting notifications (`Counter forum_ws_notifications`),
asserting the `subscribed` ack. This exercises ADR 0010 under load and validates the G13 ingress timeout fix.

**Traffic mix** (weighted per iteration, mirrors real browsing; every URL uses seeded data via `setup()`):

| Weight | Action | Endpoint(s) |
|---|---|---|
| 30% | browse category feed (keyset page 1, sometimes page 2 via returned cursor) | `GET /api/content/threads?categoryId={c}&limit=20` (+cursor follow 30% of the time) |
| 20% | open thread: detail + comment tree + reaction batch | `GET /api/content/threads/{id}` + `GET /api/content/threads/{id}/comments` + `GET /api/engagement/reactions/batch?…` (the SPA's real 3-call pattern — G22 parity note) |
| 10% | search | `GET /api/content/search?q={corpusWord}` |
| 8% | tags + popular | `GET /api/content/tags?query={prefix}` |
| 10% | authenticated: create comment | `POST /api/content/threads/{id}/comments` (body from corpus) |
| 5% | authenticated: create thread (with 1–3 tags) | `POST /api/content/threads` |
| 12% | authenticated: toggle reaction | `PUT`/`DELETE /api/engagement/reactions/thread/{id}` |
| 3% | authenticated: file upload golden path | `POST /api/files` (declare image/png, 67 B) → presigned `PUT` (the embedded 1×1 PNG — real magic bytes, so commit's `ImageProbe` passes) → `POST /api/files/{id}/commit` |
| 2% | profile + stats | `GET /api/identity/users/{id}` `GET /api/engagement/users/{id}/stats` |

Authenticated VUs: `setup()` logs in a pool of 200 seeded users (staggered ≤5 rps to respect the auth
limiter), returns `{accessToken}` per user; VUs pick a user hash-stable by `__VU`. Tokens live 15 min > any
profile duration — no refresh churn inside the run (defensible: measures the forum, not the auth stack;
B parity). Tag every request with `tags: { endpoint: '<name>' }` and set
`summaryTrendStats: ["avg","min","med","max","p(90)","p(95)","p(99)","count"]` — per-endpoint p95/p99 feed
the thesis tables directly via `handleSummary` JSON (adopt the Python repo's `===K6_SUMMARY_JSON_BEGIN===`
marker + `load/results/` convention, including the HTML report template idea if time permits — optional).

**Scripts.**
- `scripts/run-load-test.sh` (extend, keep CLI shape `run-load-test.sh [smoke|demo|stress] [BASE_URL]`):
  point at `load/k6/main.js`; while running, sample every 5 s into `load/results/samples-<stamp>.json`:
  HPA current/desired (`kubectl get hpa backend -o jsonpath=…`), `kubectl top pods` CPU/mem summed over
  backend pods (straight adaptation of the Python repo's sampler, which worked well); afterwards extract the
  summary JSON from k6 stdout into `load/results/summary-<stamp>.json`.
- **New `scripts/bench-run.sh`** — the *measured-run orchestrator*:
  1. preflight: cluster up, monitoring up, seed sentinel present, `RateLimiting` raised (`kubectl set env deployment/backend RateLimiting__Global__PermitLimit=1000000` + rollout wait) — records everything into `load/results/meta-<stamp>.json` (git SHA, image tag, node stats);
  2. warm-up: one `smoke` run (JIT, pools, caches) — discarded;
  3. N repeats (default 3) of the chosen profile with 2 min cool-down between;
  4. captures per-run: k6 summary, sampler JSON, `kubectl get events`, Prometheus range snapshots for the key queries (via `kubectl port-forward` + `curl 'http://localhost:9090/api/v1/query_range?…'` for the §10c dashboard queries) → `thesis/results/A/<date>-<profile>/`;
  5. restores the rate-limit env, prints the mean±stddev of req/s and p95 across repeats.
- Makefile: `load` target already exists; add `bench: ## Full measured benchmark (ARGS=demo|stress)`.

**Benchmark fairness checklist (print it in `bench-run.sh` output; thesis method section):**
same seed volumes as B · same resource limits on A and B backends · A and B never run simultaneously ·
warm-up run discarded · ≥3 repeats, report mean + stddev · same k6 host, same profiles, same think-time
(0.3–0.7 s random) · record git SHA + image digest + date in `meta.json` · Grafana dashboard screenshots
(cluster overview, app RED, HPA) exported per run window.

**Watch out.**
- Do NOT hit `/health/*` in load scenarios (kubelet signal pollution — the Python repo's comment is right).
- k6 must send `Content-Type: application/json` and the SPA's plural-call pattern (thread detail = 3 calls) —
  measuring a pattern the real client doesn't use would flatter A dishonestly.
- The upload path PUTs to `minio.forum.local` (presigned) — from the WSL host that resolves via /etc/hosts to
  the minikube IP; confirm before the measured run (`curl -sI` a presigned URL).
- Login storm: 200 logins × Argon2id ≈ real CPU; stagger in setup or the first samples are polluted (also
  why warm-up run exists).
- If `http_req_failed` includes 429s, the limiter raise didn't apply — abort and fix; never "benchmark" a
  rate limiter.
- WS scenario failures after exactly 60 s idle = G13 regression (ingress timeout annotation lost).

**Definition of Done.** `make load ARGS=smoke|demo|stress` all run green against the cluster; demo produces a
visible 1→2→3 HPA staircase on the 10c dashboard; `make bench ARGS=demo` leaves a complete
`thesis/results/A/<stamp>/` bundle (k6 summaries ×3, sampler, meta, Prometheus snapshots); per-endpoint
p95/p99 tables extractable from summary JSON.

**START-OF-PHASE REMINDERS.**
- *Remember:* one `main.js` with PROFILE env (delete smoke.js); weighted realistic mix incl. writes + presigned
  upload (real PNG magic bytes) + a WS-holding scenario; k6 runs on the HOST through ingress; plateaus ≥90 s
  for HPA visibility; raise the global rate limit for measured runs via bench-run.sh and record it; warm-up +
  3 repeats + archive to `thesis/results/`; never load `/health/*`.

---

## Phase 10a — Docker image pipeline & hardening

**Goal.** Two production-grade images (backend hardened further, frontend created from scratch), a tagging
scheme that ties every benchmark number to an exact build, fast rebuilds, and a documented vulnerability-scan
step.

**Depends on.** 9a merged (the image must contain the observability/presign/forwarded-headers code).

**Steps.**

1. **Backend runtime base → chiseled (G15).** Change the runtime stage to
   `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra`:
   - *chiseled* = Canonical's distroless-style Ubuntu: no shell, no package manager, no `useradd` needed —
     ships a non-root `app` user (uid 1654) and runs as it by default. Attack surface and CVE count drop hard.
   - **`-extra` variant is required**, not plain chiseled: plain lacks ICU and the app uses culture-aware
     comparisons + PostgreSQL citext interplay; without ICU .NET falls back to invariant globalization and
     subtle string behavior changes. Do not "optimize" this away.
   - Consequences to encode in the Dockerfile: drop the `RUN useradd` line (no shell to run it); keep
     `USER app` explicit for self-documentation; `EXPOSE 8080`; keep `DOTNET_EnableDiagnostics=0`.
   - **Debugging trade-off (document in Dockerfile comment):** `kubectl exec` into a chiseled container is
     impossible (no shell). The escape hatch is `kubectl debug -it <pod> --image=busybox --target=backend`
     (ephemeral containers). If that ever blocks a session, a temporary fallback to `aspnet:10.0` is one line.

   ```dockerfile
   # syntax=docker/dockerfile:1
   FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
   WORKDIR /src
   COPY backend/global.json backend/Directory.Build.props backend/Directory.Packages.props ./backend/
   COPY backend/src ./backend/src
   RUN --mount=type=cache,target=/root/.nuget/packages \
       dotnet restore backend/src/Bootstrap/Forum.Api/Forum.Api.csproj
   RUN --mount=type=cache,target=/root/.nuget/packages \
       dotnet publish backend/src/Bootstrap/Forum.Api/Forum.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

   FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
   WORKDIR /app
   COPY --from=build /app .
   USER app
   EXPOSE 8080
   ENV ASPNETCORE_URLS=http://+:8080 \
       DOTNET_EnableDiagnostics=0
   ENTRYPOINT ["dotnet", "Forum.Api.dll"]
   ```

   The `--mount=type=cache` on the NuGet folder is the build-cache strategy: unchanged package graph → restore
   is near-instant on rebuilds (BuildKit is on by default in current Docker; `deploy.sh` needs no change).
   Note the existing restore-layer split (props/config copied first) already gives layer caching for source-only
   changes; the cache mount additionally survives csproj/CPM edits.

   **Non-root uid note for k8s:** the pod `securityContext.runAsUser: 1000` in `k8s/backend/deployment.yaml`
   must change to `1654` (or better: drop `runAsUser` entirely and keep `runAsNonRoot: true` — the image's
   default user is already non-root, and PSS `restricted` only requires non-root). Do the latter; less magic.

2. **Frontend image (G4).** Frontend has no Dockerfile today. Decision: **Next.js standalone server, NOT
   static export.** Rationale: `output: 'export'` would demand `generateStaticParams` for every dynamic route
   (`/t/[id]`, `/c/[slug]`, `/u/[userId]`) which is nonsense for runtime ULIDs; the standalone Node server
   renders the CSR shell and still never talks to the .NET API (the browser does). Steps:
   - `frontend/next.config.ts`: add `output: "standalone"`.
   - `frontend/Dockerfile`:

   ```dockerfile
   # syntax=docker/dockerfile:1
   FROM node:22-alpine AS build
   WORKDIR /app
   COPY frontend/package.json frontend/package-lock.json ./
   RUN --mount=type=cache,target=/root/.npm npm ci
   COPY frontend/ .
   # NEXT_PUBLIC_* are inlined at build time — the image is environment-specific by design (documented deviation:
   # a runtime-config indirection is over-engineering for a thesis cluster with exactly one environment).
   ARG NEXT_PUBLIC_API_URL=http://forum.local/api
   ARG NEXT_PUBLIC_WS_URL=ws://forum.local/api/realtime/ws
   ENV NEXT_PUBLIC_API_URL=$NEXT_PUBLIC_API_URL NEXT_PUBLIC_WS_URL=$NEXT_PUBLIC_WS_URL
   RUN npm run build

   FROM node:22-alpine AS runtime
   WORKDIR /app
   ENV NODE_ENV=production PORT=3000 HOSTNAME=0.0.0.0
   COPY --from=build /app/.next/standalone ./
   COPY --from=build /app/.next/static ./.next/static
   COPY --from=build /app/public ./public
   USER node
   EXPOSE 3000
   CMD ["node", "server.js"]
   ```

   **Watch the API base path:** the SPA's `apiFetch` base URL becomes `http://forum.local/api` — verify the
   frontend's endpoint modules don't ALSO prefix `/api` (they call `/api/content/...` today against a host
   without a path). If they do (they do — `NEXT_PUBLIC_API_URL` default is `http://localhost:5099`, paths
   include `/api/...`), then the build arg must be `http://forum.local` (host only) and ingress keeps routing
   by `/api` prefix. **Set `NEXT_PUBLIC_API_URL=http://forum.local` — empty path — and note this trap in the
   manifest comment.** Same for WS: derive from API URL (frontend already does this when `NEXT_PUBLIC_WS_URL`
   unset — leave it unset in the image; delete the ARG).
   - Add `frontend/.dockerignore` (`node_modules`, `.next`, `design-reference`, `*.md`).
   - With TLS (10b) rebuild with `https://forum.local` — the Makefile target parameterizes it.

3. **Tagging & versioning strategy (G15).** Keep `imagePullPolicy: Never` + in-minikube builds (right call
   for this project — no registry hop). Strategy:
   - `IMAGE_TAG` becomes `git-<short-sha>` computed in `lib.sh` (`IMAGE_TAG=${IMAGE_TAG:-git-$(git -C "$REPO_ROOT" rev-parse --short HEAD)}`),
     with a `local` fallback for dirty trees (append `-dirty` when `git status --porcelain` non-empty).
   - `deploy.sh` builds `forum-dotnet-api:$IMAGE_TAG` AND retags `:latest-local`, then **pins the Deployment
     to the SHA tag** via `kubectl set image deployment/backend backend=forum-dotnet-api:$IMAGE_TAG` after
     apply (manifests keep a stable placeholder tag; the set-image makes rollouts + rollbacks explicit:
     `kubectl rollout undo deployment/backend`).
   - `bench-run.sh` (9c) records `IMAGE_TAG` + `docker image inspect --format '{{.Id}}'` into `meta.json` —
     every thesis number maps to an exact build.
4. **Vulnerability scanning (documented manual/CI step).** New `scripts/scan-image.sh`:
   `trivy image --severity HIGH,CRITICAL --ignore-unfixed forum-dotnet-api:$IMAGE_TAG` (same for frontend);
   Makefile `scan:` target; README note that this runs on demand (no CI wiring in this phase — the repo's CI
   only runs build/test today; a `security.yml` job step is a 5-line future add, listed not built). Trivy
   install via its apt repo goes into `preflight.sh` as an optional check (warn-only, like k6).
5. **compose parity check.** Uncomment/verify the `api:` service in `compose.yaml` still builds with the
   chiseled image (no shell → healthcheck must be TCP or absent, not `curl`).

**Watch out.**
- Chiseled + `readOnlyRootFilesystem: true` (already set in k8s): ASP.NET needs a writable temp for
  uploads/buffering edge cases — the app streams no request bodies to disk today, but add an `emptyDir` at
  `/tmp` in the deployment anyway (10b does it); cheap insurance.
- `-extra` (ICU) not plain chiseled — see step 1.
- Frontend: the `NEXT_PUBLIC_API_URL` path-prefix trap in step 2 is the #1 way to ship a frontend that 404s
  every call. Verify with one `docker run -p 3000:3000` + browser network tab before writing manifests.
- Don't switch the SDK stage to alpine/chiseled — build stage size is irrelevant and glibc tooling is safer.

**Definition of Done.** Both images build; backend runs the full integration suite when swapped into compose
(`api:` service); frontend serves `/` locally and calls the right API origin; `make scan` reports (and the
report is committed to `docs/runbooks/` once as a baseline); `deploy.sh` deploys the SHA tag and
`kubectl rollout undo` works; rebuild after a 1-line code change reuses the NuGet/npm caches (observe timing).

**START-OF-PHASE REMINDERS.**
- *Remember:* runtime = `aspnet:10.0-noble-chiseled-extra` (uid 1654, no shell — debug via `kubectl debug`),
  drop `runAsUser: 1000` from the deployment; frontend = Next standalone on node:22-alpine, and
  `NEXT_PUBLIC_API_URL` must be the HOST WITHOUT `/api` path; tags = `git-<sha>` + set-image for rollbacks;
  trivy stays a scripted manual step.

---

## Phase 10b — Kubernetes core: infra manifests, security, networking

**Goal.** A `make mk-deploy` that brings up the ENTIRE stack (today it deploys a backend that can never
become ready — G3/G14), hardened to PSS `restricted`, with enforced least-privilege NetworkPolicies, TLS,
correct pool math, and graceful rollouts. Manifest style: keep the repo's existing hand-rolled single-line-map
YAML with heavy comments; no Helm for app/infra components (Helm is reserved for the monitoring stack, per
`infrastructure/monitoring/README.md`).

**Depends on.** 10a images; 9a code (presign endpoint, forwarded headers) — the manifests below reference
their config keys.

> **IMPLEMENTED — Phase 10b code-complete + verified live (2026-07-11).** Deviations/decisions that
> supersede the sketches below (everything else landed as written):
>
> - **Image pinning: apply-time sed substitution, NOT `kubectl set image`.** `deploy.sh apply_with_tag()`
>   rewrites the manifests' `:local` placeholder to `$IMAGE_TAG` (git-SHA) in the apply pipe. One rollout
>   per deploy instead of two (apply would revert to :local, set-image would roll again), Jobs ride the
>   same exact tag, rollout history still shows SHAs (undo verified). `reset-db.sh`/`seed-test-data.sh
>   --cluster` pin Jobs to the LIVE backend's image instead.
> - **kubelet's non-numeric-user trap hit BOTH images (found live).** kubelet cannot verify
>   `runAsNonRoot: true` against a non-numeric image USER → CreateContainerConfigError. Backend: 10a's
>   `USER app` line had *replaced* the base image's numeric Config.User=1654 with the string "app" —
>   fixed at the source (`USER 1654` in the root Dockerfile; 10a's "don't pin runAsUser in manifests"
>   guidance now actually works). Frontend: the node image ships USER "node", so its deployment pins
>   `runAsUser: 1000` (we don't own that Dockerfile). ro-rootfs stays true with an emptyDir at
>   `/app/.next/cache` (the plan's preferred option).
> - **Migration/seed Jobs also carry `backend-secrets`**: the host builds full DI before branching on the
>   CLI arg, so the 9a Production Jwt fail-fast fires even for `migrate` — a Job without the signing key
>   crashes at registration.
> - **MinIO pinned** to `RELEASE.2025-09-07T16-13-09Z` (the exact build compose runs locally); mc Job
>   pinned to `RELEASE.2025-08-13T08-35-41Z`; mc runs under an explicit restricted securityContext
>   (image defaults to root) with `MC_CONFIG_DIR=/tmp/.mc`; the Job pod's `app: minio-setup` label has
>   its own entry in 40-minio-allow (netpol would otherwise block the bucket bootstrap).
> - **Windows access architecture (new first-class deliverable, runbook §4):** verified empirically —
>   `minikube ip` is NOT reachable from Windows (docker bridge inside the WSL VM; TCP+ICMP fail), WSL2
>   localhost forwarding IS (curl.exe against a 127.0.0.1-bound WSL listener works; `.wslconfig` here
>   explicitly sets localhostForwarding=true). Everything Windows-facing therefore rides
>   `scripts/dev-tunnels.sh` (`make tunnels`): table-driven port-forwards, local ports = remote+10000
>   (never compose's published ports; Postgres tunnel = 15432, DataGrip trap documented), ingress on real
>   443/80 via a one-time `ip_unprivileged_port_start=80` sysctl (8443 fallback + printed fix otherwise),
>   Windows hosts file → 127.0.0.1 (NOT minikube ip), mkcert CA imported into the WINDOWS store
>   (certutil flow printed by mkcert-tls.sh). Grafana/Prometheus are one array line each in 10c.
> - **Swagger UI is Development-only** (Program.cs gates MapOpenApi/UseSwaggerUI) — the cluster runs
>   Production, so the backend tunnel serves /api + /health + /metrics but 404s /swagger. By design;
>   documented in dev-tunnels output + runbook.
> - **`.wslconfig` prerequisite flagged:** this machine currently grants WSL 10GB → the §1 target
>   MINIKUBE_MEMORY=10240 cannot fit; verified with 8192 (app stack sized ~1.5Gi requests). Bump
>   .wslconfig to 12GB + .env to 10240 before 10c/monitoring + measured runs. preflight.sh checks both
>   RAM headroom and localhostForwarding now.
> - **HPA `spec.replicas` dropped from the deployment manifest** (HPA owns scale; a literal would snap
>   the fleet back on every apply).

### Steps

**1. Namespace + Pod Security Standards (G12/G21).** `k8s/namespace.yaml` gains PSS labels:

```yaml
apiVersion: v1
kind: Namespace
metadata:
  name: forum-dotnet
  labels:
    app.kubernetes.io/part-of: forum-dotnet
    pod-security.kubernetes.io/enforce: restricted
    pod-security.kubernetes.io/warn: restricted
    pod-security.kubernetes.io/audit: restricted
```

Every pod spec in this namespace therefore needs: `runAsNonRoot: true`, `seccompProfile: { type: RuntimeDefault }`,
`allowPrivilegeEscalation: false`, `capabilities: { drop: ["ALL"] }`. The backend deployment already has most;
**add `seccompProfile` to it** (it's missing — PSS restricted rejects the pod without it). Exceptions:
- `monitoring` namespace (created by Helm in 10c): label `enforce: baseline` only — node-exporter needs
  hostNetwork/hostPath and Alloy reads kubelet logs; both violate `restricted`. Documented exception.
- Postgres/RabbitMQ/MinIO: all three run fine under `restricted` with the securityContexts below — no exception needed.

**2. Secrets (one per concern, all with a committed `*.example.yaml` and gitignored real file — extend the
existing `k8s/postgres/secret.example.yaml` pattern; `deploy.sh` keeps its generate-if-missing behavior and
now generates ALL of them from `.env`):**

| Secret | Keys | Consumed by |
|---|---|---|
| `postgres-credentials` (exists) | `POSTGRES_DB/USER/PASSWORD`, `CONNECTION_STRING` (now WITH `Maximum Pool Size=30;` — see §7 below) | postgres, backend, jobs, postgres-exporter (10c) |
| `rabbitmq-credentials` (new) | `RABBITMQ_DEFAULT_USER=forum`, `RABBITMQ_DEFAULT_PASS=<gen>`, plus `RabbitMq__Username/RabbitMq__Password` duplicates for the backend env | rabbitmq, backend, jobs |
| `minio-credentials` (new) | `MINIO_ROOT_USER`, `MINIO_ROOT_PASSWORD`, `Storage__AccessKey/Storage__SecretKey` duplicates | minio, backend, create-bucket job |
| `backend-secrets` (new) | `Jwt__SigningKey` (32+ random bytes, `openssl rand -base64 48`) | backend, jobs |
| `forum-tls` (new, via mkcert — §9) | `tls.crt`, `tls.key` | ingress |

**Recommendation on sealed-secrets/external-secrets: DON'T.** This is a single-developer minikube with no
GitOps controller and no cloud KMS; plain namespaced Secrets generated from `.env` by `deploy.sh` are the
honest fit. Sealed-secrets would add a controller + key-backup problem to protect secrets that are… generated
dev values. State exactly this in the thesis ("in a multi-tenant/GitOps setup we would move to
external-secrets"), and enforce the real rule that matters: **no real secret ever committed** (already policy).

RabbitMQ note (G14): creating `forum` via `RABBITMQ_DEFAULT_USER/PASS` sidesteps the loopback-only `guest`
restriction. Backend's `RabbitMq__Username/Password` come from the same secret so they can't drift.

**3. Postgres hardening (G2).** Rewrite `k8s/postgres/statefulset.yaml`:

```yaml
apiVersion: apps/v1
kind: StatefulSet
metadata: { name: postgres, namespace: forum-dotnet }
spec:
  serviceName: postgres
  replicas: 1
  selector: { matchLabels: { app: postgres } }
  template:
    metadata: { labels: { app: postgres } }
    spec:
      serviceAccountName: postgres
      securityContext:
        runAsNonRoot: true
        runAsUser: 999            # postgres uid in the official image
        runAsGroup: 999
        fsGroup: 999
        seccompProfile: { type: RuntimeDefault }
      containers:
        - name: postgres
          image: postgres:17
          # Benchmark-stable tuning; defaults are conservative. max_connections stays 100 — see pool math.
          args: ["-c", "shared_buffers=256MB", "-c", "effective_cache_size=512MB", "-c", "max_connections=100"]
          envFrom: [{ secretRef: { name: postgres-credentials } }]
          env: [{ name: PGDATA, value: /var/lib/postgresql/data/pgdata }]   # subdir: fsGroup+PVC lost+found quirk
          ports: [{ containerPort: 5432 }]
          readinessProbe: { exec: { command: ["pg_isready", "-U", "forum"] }, initialDelaySeconds: 5, periodSeconds: 10 }
          livenessProbe:  { exec: { command: ["pg_isready", "-U", "forum"] }, initialDelaySeconds: 30, periodSeconds: 20 }
          resources:
            requests: { cpu: "250m", memory: "512Mi" }
            limits:   { cpu: "1000m", memory: "1Gi" }
          securityContext:
            allowPrivilegeEscalation: false
            readOnlyRootFilesystem: true
            capabilities: { drop: ["ALL"] }
          volumeMounts:
            - { name: data, mountPath: /var/lib/postgresql/data }
            - { name: run,  mountPath: /var/run/postgresql }   # unix socket dir — required for ro-rootfs
            - { name: tmp,  mountPath: /tmp }
      volumes:
        - { name: run, emptyDir: {} }
        - { name: tmp, emptyDir: {} }
  volumeClaimTemplates:
    - metadata: { name: data }
      spec: { accessModes: ["ReadWriteOnce"], resources: { requests: { storage: 4Gi } } }
```

**Where the official image constrains hardening (explicit, as demanded):** the entrypoint runs `initdb`/chmod
as the container user; with `runAsUser: 999` + `fsGroup: 999` that works, BUT `readOnlyRootFilesystem: true`
only works with the two `emptyDir`s above (`/var/run/postgresql` for the socket, `/tmp`). If the image ever
fails on a ro-rootfs edge (e.g. extensions writing to `/usr/share`), the sanctioned fallback is
`readOnlyRootFilesystem: false` on THIS container only, with a comment — everything else stays. PVC bumped
2Gi→4Gi (seeded DB + WAL under stress; still tiny).

**4. RabbitMQ (G3/G14).** New `k8s/rabbitmq/statefulset.yaml` + `service.yaml` + `configmap.yaml`:

```yaml
# k8s/rabbitmq/configmap.yaml — enable the prometheus plugin + per-queue metrics (10c dashboards need per-queue depth)
apiVersion: v1
kind: ConfigMap
metadata: { name: rabbitmq-config, namespace: forum-dotnet }
data:
  enabled_plugins: "[rabbitmq_management,rabbitmq_prometheus]."
  rabbitmq.conf: |
    prometheus.return_per_object_metrics = true
    vm_memory_high_watermark.relative = 0.6
---
# k8s/rabbitmq/statefulset.yaml
apiVersion: apps/v1
kind: StatefulSet
metadata: { name: rabbitmq, namespace: forum-dotnet }
spec:
  serviceName: rabbitmq
  replicas: 1
  selector: { matchLabels: { app: rabbitmq } }
  template:
    metadata: { labels: { app: rabbitmq } }
    spec:
      serviceAccountName: rabbitmq
      securityContext: { runAsNonRoot: true, runAsUser: 999, runAsGroup: 999, fsGroup: 999, seccompProfile: { type: RuntimeDefault } }
      containers:
        - name: rabbitmq
          image: rabbitmq:4-management
          envFrom: [{ secretRef: { name: rabbitmq-credentials } }]
          ports: [{ containerPort: 5672 }, { containerPort: 15672 }, { containerPort: 15692 }]
          readinessProbe: { exec: { command: ["rabbitmq-diagnostics", "check_port_connectivity"] }, initialDelaySeconds: 20, periodSeconds: 15, timeoutSeconds: 10 }
          livenessProbe:  { exec: { command: ["rabbitmq-diagnostics", "status"] }, initialDelaySeconds: 60, periodSeconds: 30, timeoutSeconds: 15 }
          resources:
            requests: { cpu: "100m", memory: "256Mi" }
            limits:   { cpu: "500m", memory: "512Mi" }
          securityContext: { allowPrivilegeEscalation: false, readOnlyRootFilesystem: false, capabilities: { drop: ["ALL"] } }
          # ro-rootfs deliberately OFF: rabbit writes /var/lib/rabbitmq (PVC) AND /etc/rabbitmq runtime state;
          # chasing every write path is not worth it for a broker — documented exception, still non-root+no-caps.
          volumeMounts:
            - { name: data, mountPath: /var/lib/rabbitmq }
            - { name: config, mountPath: /etc/rabbitmq/conf.d/10-forum.conf, subPath: rabbitmq.conf }
            - { name: config, mountPath: /etc/rabbitmq/enabled_plugins, subPath: enabled_plugins }
      volumes:
        - { name: config, configMap: { name: rabbitmq-config } }
  volumeClaimTemplates:
    - metadata: { name: data }
      spec: { accessModes: ["ReadWriteOnce"], resources: { requests: { storage: 1Gi } } }
---
# k8s/rabbitmq/service.yaml
apiVersion: v1
kind: Service
metadata:
  name: rabbitmq
  namespace: forum-dotnet
  labels: { app: rabbitmq }
spec:
  selector: { app: rabbitmq }
  ports:
    - { name: amqp, port: 5672, targetPort: 5672 }
    - { name: management, port: 15672, targetPort: 15672 }
    - { name: prometheus, port: 15692, targetPort: 15692 }
```

Management UI: never exposed via ingress; `make port-forward` (§11) → `localhost:15672`.

**5. MinIO (G3/G5/G20).** New `k8s/minio/statefulset.yaml` + `service.yaml` + `create-bucket-job.yaml`:

```yaml
# k8s/minio/statefulset.yaml (essentials; same securityContext shape as rabbitmq, uid 1000 works for minio)
apiVersion: apps/v1
kind: StatefulSet
metadata: { name: minio, namespace: forum-dotnet }
spec:
  serviceName: minio
  replicas: 1
  selector: { matchLabels: { app: minio } }
  template:
    metadata: { labels: { app: minio } }
    spec:
      serviceAccountName: minio
      securityContext: { runAsNonRoot: true, runAsUser: 1000, runAsGroup: 1000, fsGroup: 1000, seccompProfile: { type: RuntimeDefault } }
      containers:
        - name: minio
          image: minio/minio:latest        # pin a RELEASE.2026-xx tag at implementation time — 'latest' breaks reproducibility
          args: ["server", "/data", "--console-address", ":9001"]
          envFrom: [{ secretRef: { name: minio-credentials } }]
          env:
            - { name: MINIO_PROMETHEUS_AUTH_TYPE, value: "public" }          # 10c scrape without a bearer token
            - { name: MINIO_API_CORS_ALLOW_ORIGIN, value: "http://forum.local,https://forum.local" }  # G20: browser presigned PUT preflight
          ports: [{ containerPort: 9000 }, { containerPort: 9001 }]
          readinessProbe: { httpGet: { path: /minio/health/ready, port: 9000 }, initialDelaySeconds: 5 }
          livenessProbe:  { httpGet: { path: /minio/health/live,  port: 9000 }, initialDelaySeconds: 15 }
          resources:
            requests: { cpu: "100m", memory: "256Mi" }
            limits:   { cpu: "500m", memory: "512Mi" }
          securityContext: { allowPrivilegeEscalation: false, readOnlyRootFilesystem: true, capabilities: { drop: ["ALL"] } }
          volumeMounts: [{ name: data, mountPath: /data }, { name: tmp, mountPath: /tmp }]
      volumes: [{ name: tmp, emptyDir: {} }]
  volumeClaimTemplates:
    - metadata: { name: data }
      spec: { accessModes: ["ReadWriteOnce"], resources: { requests: { storage: 2Gi } } }
```

`create-bucket-job.yaml`: a Job running `minio/mc` — `mc alias set local http://minio:9000 $USER $PASS && mc mb -p local/forum`
(idempotent `-p`), envFrom the secret. `deploy.sh` runs it after minio rollout. *(The app's `EnsureBucketAsync`
exists, but an explicit Job keeps the backend free of bucket-admin rights — least privilege.)*

**Presigned URL exposure — decision (G5): via ingress host `minio.forum.local`, NOT NodePort.** The Python
repo used NodePort 30900 + an `0.0.0.0/0` NetworkPolicy hole; ingress is cleaner: one entry point, TLS for
free, and the NetworkPolicy stays "ingress-controller → minio:9000" instead of world-open. Backend config gets
`Storage__PublicEndpoint: "minio.forum.local"` + `Storage__PublicUseSsl: "false"` (flip with TLS §9; requires
the mkcert cert to include `minio.forum.local` as a SAN). Browser flow: presigned URL
`http://minio.forum.local/forum/<key>?X-Amz-…` → ingress → minio:9000; signature valid because the presign
client (9a) signed for exactly that host. **Ingress annotation `proxy-body-size` must exceed the 5 MiB upload
cap** (`nginx.ingress.kubernetes.io/proxy-body-size: "10m"` on the minio ingress rule).

**6. Frontend manifests (G4).** New `k8s/frontend/deployment.yaml` + `service.yaml`: standard 1-replica
Deployment, image `forum-dotnet-web:$IMAGE_TAG`, port 3000, probes `httpGet /` (readiness 5 s, liveness 15 s),
resources per §1 (50m/300m, 128Mi/256Mi), full restricted securityContext (`runAsNonRoot`, node user is
non-root, ro-rootfs **false** — Next writes `.next/cache`; alternatively mount emptyDir at `/app/.next/cache`
and keep ro-rootfs true — do that), Service `frontend:80 → 3000` (matches the existing ingress backend ref).

**7. Backend deployment updates + THE POOL MATH (G8/G11/G17).** Edits to `k8s/backend/`:

- **Pool math (show this in the manifest comment):** Npgsql pools per unique connection string per process.
  All 5 DbContexts + health check + seeder share ONE string → one pool per pod, default `Maximum Pool Size=100`.
  Worst case today: 3 pods × 100 = **300 potential connections vs `max_connections=100` → guaranteed
  exhaustion** (G8). Fix: `CONNECTION_STRING` gains `Maximum Pool Size=30;`:
  `3 replicas × 30 = 90` app connections + postgres-exporter (5) + migration/seed Job (30 transient, never
  concurrent with peak by procedure) + psql sessions (≤3) = **≤ 98 ≤ 100**. Constraint satisfied with
  `max_connections` left at the Postgres default (100) — deliberately NOT raised: raising it costs RAM per
  connection and hides pool bugs; the thesis point is the documented inequality `replicas × pool ≤ max_connections`.
  If stress runs actually saturate 30/pod (watch `forum` pool waits + postgres connections dashboard), the
  knob order is: reduce HPA max to 2 → raise pool to 45 → only then raise `max_connections`.
- configmap.yaml: `Otlp__Endpoint: "http://tempo.monitoring.svc.cluster.local:4317"` (G11),
  `Storage__PublicEndpoint: "minio.forum.local"`, `Storage__Endpoint: "minio:9000"` (already),
  `RabbitMq__Host: "rabbitmq"` (already), `Cors__AllowedOrigins__0: "http://forum.local"` +
  `__1: "https://forum.local"` (the SPA origin — TODAY'S CONFIG WOULD CORS-BLOCK THE DEPLOYED FRONTEND;
  appsettings only allows localhost), `ForwardedHeaders__KnownNetworks__0: "10.244.0.0/16"`.
- deployment.yaml: `serviceAccountName: backend`; `automountServiceAccountToken: false`; drop `runAsUser: 1000`
  (10a chiseled uid); add `seccompProfile: { type: RuntimeDefault }` (PSS); add `emptyDir` at `/tmp`;
  `envFrom` += `backend-secrets`, `rabbitmq-credentials`, `minio-credentials`;
  **strategy: `rollingUpdate: { maxSurge: 1, maxUnavailable: 0 }`** (G17 — never dip below current capacity);
  `terminationGracePeriodSeconds: 40` + `lifecycle: { preStop: { sleep: { seconds: 5 } } }` (endpoint
  propagation before SIGTERM; pairs with 9a's 25 s ShutdownTimeout: 5 + 25 < 40). *(preStop `sleep` field:
  k8s ≥1.30 native; chiseled has no shell so `exec sleep` is unavailable — minikube's k8s is current, fine.)*
- hpa.yaml: keep CPU 70% / min 1 / max 3 (validated by the pool math above — 3 is also the RAM budget cap);
  add explicit behavior so the demo profile's staircase is deterministic:

  ```yaml
  behavior:
    scaleUp:   { stabilizationWindowSeconds: 30,  policies: [{ type: Pods, value: 1, periodSeconds: 30 }] }
    scaleDown: { stabilizationWindowSeconds: 180, policies: [{ type: Pods, value: 1, periodSeconds: 60 }] }
  ```

  **Custom-metrics HPA (prometheus-adapter) — considered, REJECTED (recorded decision, G18):** a
  latency/queue-depth signal is theoretically better, but on this stack CPU tracks load almost linearly
  (Argon2id logins + JSON serialization are CPU-bound), prometheus-adapter costs ~100–200Mi + a config file
  that is its own project, and a p95-driven HPA with 15 s windows oscillates without careful tuning. CPU@70%
  + the behavior block is the professional choice at this scale; the thesis gets a paragraph, not a component.
- pdb.yaml: keep backend `minAvailable: 1`. **Do NOT add PDBs for the single-replica StatefulSets** —
  a `minAvailable: 1` PDB on a 1-replica postgres makes `kubectl drain` impossible and buys nothing on a
  single-node cluster (there is nowhere to evacuate to). Documented decision.
- migration-job.yaml / seed-job.yaml: `serviceAccountName: backend`, same secret wiring, resources
  (250m/500m, 256Mi/512Mi), `activeDeadlineSeconds: 600`.

**8. ServiceAccounts & RBAC (G12).** New `k8s/rbac.yaml`: ServiceAccounts `backend`, `frontend`, `postgres`,
`rabbitmq`, `minio` — every one with `automountServiceAccountToken: false`. **No Roles/RoleBindings at all:**
none of these workloads talks to the Kubernetes API, so least privilege = no token mounted, zero grants.
(Anything needing API access — Prometheus Operator, kube-state-metrics — ships its own RBAC via Helm in 10c.)
This is stronger than inventing empty Roles; say so in the file's comment.

**9. Ingress + TLS + WebSocket (G13).** Extend `k8s/ingress/ingress.yaml`:

```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: forum
  namespace: forum-dotnet
  annotations:
    nginx.ingress.kubernetes.io/ssl-redirect: "true"
    # WebSocket: /api/realtime/ws is long-lived; default 60s proxy timeouts kill idle sockets (G13).
    nginx.ingress.kubernetes.io/proxy-read-timeout: "3600"
    nginx.ingress.kubernetes.io/proxy-send-timeout: "3600"
spec:
  tls:
    - hosts: [forum.local, minio.forum.local, grafana.forum.local]
      secretName: forum-tls
  rules:
    - host: forum.local
      http:
        paths:
          - { path: /api, pathType: Prefix, backend: { service: { name: backend,  port: { number: 80 } } } }
          - { path: /,    pathType: Prefix, backend: { service: { name: frontend, port: { number: 80 } } } }
    - host: minio.forum.local
      http:
        paths:
          - { path: /, pathType: Prefix, backend: { service: { name: minio, port: { number: 9000 } } } }
```

*(minio rule additionally gets `proxy-body-size: 10m` — nginx annotations are per-Ingress, so split the minio
host into its own small Ingress object `k8s/minio/ingress.yaml` to scope the annotation. grafana.forum.local
rule ships with 10c.)*

**TLS via mkcert** (new `scripts/mkcert-tls.sh`): `mkcert -install` (once, trusts the local CA in the WSL +
Windows store — document that browsers on Windows need the CA installed there too),
`mkcert forum.local minio.forum.local grafana.forum.local`, then
`kubectl -n forum-dotnet create secret tls forum-tls --cert=… --key=…` (+ same secret in `monitoring` ns for
grafana if its ingress lives there). `/etc/hosts` gains all three names → `minikube ip`. After TLS: rebuild
frontend image with `https://forum.local` (10a ARG), flip `Storage__PublicUseSsl` to `true`, add the https
origins to CORS config. **Self-signed fallback** (no mkcert): `openssl req -x509 -newkey rsa:2048 -nodes
-days 365 -subj "/CN=forum.local" -addext "subjectAltName=DNS:forum.local,DNS:minio.forum.local,DNS:grafana.forum.local"`
— browsers will warn; mkcert is strongly preferred.

**10. NetworkPolicies (G1) — real allow-rules, enforced.** First, **enforcement prerequisite:**
`scripts/setup-minikube.sh` gains `--cni=calico` (minikube's default CNI ignores NetworkPolicy objects
entirely — the Python repo documented this exact trap; without calico the policies are decoration).
Budget for calico is in §1. New files in `k8s/network-policies/` (default-deny stays as-is):

```yaml
# 10-backend-allow.yaml — backend accepts: ingress-nginx (API+WS), monitoring (metrics scrape). Nothing else.
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata: { name: backend-allow, namespace: forum-dotnet }
spec:
  podSelector: { matchLabels: { app: backend } }
  policyTypes: ["Ingress"]
  ingress:
    - from:
        - namespaceSelector: { matchLabels: { kubernetes.io/metadata.name: ingress-nginx } }
        - namespaceSelector: { matchLabels: { kubernetes.io/metadata.name: monitoring } }
      ports: [{ protocol: TCP, port: 8080 }]
---
# 20-postgres-allow.yaml — DB reachable ONLY from backend pods (deployment + jobs share app label) and postgres-exporter.
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata: { name: postgres-allow, namespace: forum-dotnet }
spec:
  podSelector: { matchLabels: { app: postgres } }
  policyTypes: ["Ingress"]
  ingress:
    - from:
        - podSelector: { matchLabels: { app: backend } }
        - namespaceSelector: { matchLabels: { kubernetes.io/metadata.name: monitoring } }   # postgres-exporter (10c)
      ports: [{ protocol: TCP, port: 5432 }]
---
# 30-rabbitmq-allow.yaml — AMQP from backend; prometheus port from monitoring; management NOT allowed (port-forward bypasses netpol via the API server, which is exactly the intended admin path).
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata: { name: rabbitmq-allow, namespace: forum-dotnet }
spec:
  podSelector: { matchLabels: { app: rabbitmq } }
  policyTypes: ["Ingress"]
  ingress:
    - from: [{ podSelector: { matchLabels: { app: backend } } }]
      ports: [{ protocol: TCP, port: 5672 }]
    - from: [{ namespaceSelector: { matchLabels: { kubernetes.io/metadata.name: monitoring } } }]
      ports: [{ protocol: TCP, port: 15692 }]
---
# 40-minio-allow.yaml — S3 API from backend AND from ingress-nginx (browser presigned traffic — the deliberate
# improvement over the reference repo's 0.0.0.0/0 hole); metrics from monitoring.
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata: { name: minio-allow, namespace: forum-dotnet }
spec:
  podSelector: { matchLabels: { app: minio } }
  policyTypes: ["Ingress"]
  ingress:
    - from:
        - podSelector: { matchLabels: { app: backend } }
        - namespaceSelector: { matchLabels: { kubernetes.io/metadata.name: ingress-nginx } }
        - namespaceSelector: { matchLabels: { kubernetes.io/metadata.name: monitoring } }
      ports: [{ protocol: TCP, port: 9000 }]
---
# 50-frontend-allow.yaml — only the ingress controller talks to the SPA shell.
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata: { name: frontend-allow, namespace: forum-dotnet }
spec:
  podSelector: { matchLabels: { app: frontend } }
  policyTypes: ["Ingress"]
  ingress:
    - from: [{ namespaceSelector: { matchLabels: { kubernetes.io/metadata.name: ingress-nginx } } }]
      ports: [{ protocol: TCP, port: 3000 }]
```

**Jobs need the `app: backend` label** on their pod template so 20-postgres-allow admits them — add
`labels: { app: backend }` to migration-job/seed-job templates (they currently have none).

**Egress: deliberately NOT default-denied.** Recorded decision: egress lockdown demands DNS-allow rules +
kube-apiserver carve-outs and (on minikube, one node, one tenant) demonstrates nothing an ingress default-deny
doesn't already prove for the thesis. Revisit line: "production would add default-deny egress with explicit
DNS/postgres/rabbitmq/minio/tempo egress rules per pod" — one sentence in the thesis, zero YAML here.

Flip the default: `.env` `APPLY_NETWORK_POLICIES=true`; `deploy.sh` applies the whole folder always (the
warn-branch goes away); teardown of the old caveat text in `docs/runbooks/wsl-minikube-setup.md`.
**Enforcement verification** becomes part of the deploy DoD: a `kubectl run netpol-test --rm -it
--image=busybox -n forum-dotnet -- nc -zv -w2 postgres 5432` must FAIL, while backend stays ready.

**11. `scripts/deploy.sh` — extended apply order (extend, don't rewrite; the step/ok/die helpers stay):**

```
build backend image → build frontend image
→ namespace (now with PSS labels)
→ secrets (generate-if-missing ×4 + tls check with pointer to mkcert-tls.sh)
→ rbac.yaml (ServiceAccounts)
→ postgres + rabbitmq (+config) + minio  → wait all three rollouts
→ create-bucket Job → wait
→ migration Job → wait (exists)
→ [--seed flag] seed Job → wait
→ backend (configmap, deployment, service, hpa, pdb) → set image to SHA tag → rollout wait
→ frontend (deployment, service) → rollout wait
→ ingress (app + minio) 
→ network-policies (now unconditional)
→ print URLs incl. https + minio + note about monitoring-up.sh
```

`reset-db.sh`: also delete the seed Job. `teardown.sh`: unchanged. `preflight.sh`: add mkcert + trivy +
helm as warn-only checks.

**Watch out.**
- Apply order matters exactly as listed: PSS labels before pods (rejects come at admission), secrets before
  consumers, infra before migration Job, migration before backend (ADR 0005), bucket Job before backend
  (its `EnsureBucketAsync` startup would otherwise race the netpol… actually verify whether the backend still
  calls EnsureBucket at startup — if yes it needs minio ready anyway; readiness gates keep this safe).
- PSS `restricted` rejects pods missing `seccompProfile` — the CURRENT backend deployment would be rejected
  the moment the namespace label lands; patch the deployment in the same session as the namespace.
- Calico on minikube: start FRESH (`minikube delete` first) — CNI can't be swapped on a live profile.
  This wipes the cluster: sequence it before any long-lived state matters (it doesn't — everything is scripted).
- `imagePullPolicy: Never` + SHA tags: the image must be built into minikube's docker BEFORE set-image, or
  the rollout wedges on ErrImageNeverPull — deploy.sh order handles it; don't hand-run set-image.
- Verify `10.244.0.0/16` is the actual pod CIDR under calico (`kubectl cluster-info dump | grep -m1 cluster-cidr`)
  — if not, fix the ForwardedHeaders ConfigMap value (9a made it configurable for exactly this).
- MinIO `latest` → pin a release tag when writing the manifest (reproducibility; noted inline).
- After enabling TLS, `Secure` cookie semantics: the refresh cookie is set by the API — confirm
  `CookieSecurePolicy`/`SameSite` config still matches an https origin (frontend and API share the
  `forum.local` site → SameSite=Lax keeps working; the cookie should become `Secure` under https).

**Definition of Done.** From `minikube delete`: `make mk-up && make mk-deploy ARGS=--seed` ends with ALL pods
Ready (backend 1+, frontend, postgres, rabbitmq, minio), migration+bucket+seed Jobs Complete, `/health/ready`
200 via `https://forum.local/api/../health/ready`? (health is not under `/api` — verify via port-forward
instead), full user journey works in a browser against `https://forum.local` (register → login → browse seeded
feed → open thread → comment → like → upload an avatar image — this exercises presign/CORS/G5/G20 end-to-end
→ WS pill shows LIVE and a second browser sees the comment live), netpol probe test fails as specified,
`kubectl get pods -n forum-dotnet -o jsonpath='{..securityContext}'` shows the restricted posture, PSS warns
zero, `kubectl rollout undo deployment/backend` rolls back cleanly.

**START-OF-PHASE REMINDERS.**
- *Remember:* the current deploy is BROKEN by design gaps (no rabbitmq/minio manifests, guest-loopback,
  presign host, CORS origins) — this phase exists to fix G1–G6/G8/G11–G14/G17/G20 in YAML; keep the repo's
  manifest style; calico or netpols are theater; pool math `3×30+overhead ≤ 100` goes into a manifest comment;
  Jobs carry `app: backend` for the postgres netpol; PSS restricted needs seccompProfile everywhere; minio
  presign via `minio.forum.local` ingress host (NOT NodePort, NOT 0.0.0.0/0); no Roles — just tokenless
  ServiceAccounts; plain Secrets are the recorded recommendation (no sealed-secrets on minikube).

---

## Phase 10c — Monitoring stack (Helm, dashboards, alerts, correlation)

**Goal.** kube-prometheus-stack + Loki + Tempo (the stack `infrastructure/monitoring/README.md` already
decided) installed reproducibly via Helm values files in `k8s/monitoring/`, seven concrete Grafana dashboards,
alert rules, and metric↔trace↔log correlation — all inside the §1 budget.

**Depends on.** 10b (cluster up, netpols reference the `monitoring` namespace), 9a (the `forum_*` series and
JSON logs the dashboards/derived-fields consume).

**Component choices (deviations from the Python repo, with reasons):**
- **Loki via the `grafana/loki` chart in `SingleBinary` mode** — the Python repo used `loki-stack`, which is
  deprecated/frozen upstream; single-binary Loki on filesystem storage is the current supported minimal shape.
- **Grafana Alloy as log shipper, not Promtail** — Promtail is EOL'd upstream (superseded by Alloy). Alloy
  runs as a 1-pod DaemonSet and also positions us to ship its config as code.
- **Tempo via `grafana/tempo` chart** (single binary, OTLP gRPC 4317) — backend exports OTLP directly;
  **no otel-collector** component (one less pod in the budget; the collector adds value only with multi-target
  pipelines we don't have). This resolves G11 by pointing `Otlp__Endpoint` at Tempo (done in 10b configmap).
- **postgres-exporter via `prometheus-community/prometheus-postgres-exporter` chart** (the (c) database
  dashboard is impossible without it).
- **Alertmanager disabled** (§1; rules still evaluate — visible in Prometheus UI + Grafana Alerting).

### Steps

**1. Files.** All values live in `k8s/monitoring/` (replacing the README stub):
`values-kube-prometheus-stack.yaml`, `values-loki.yaml`, `values-alloy.yaml`, `values-tempo.yaml`,
`values-postgres-exporter.yaml`, `servicemonitors.yaml`, `prometheus-rules.yaml`,
`grafana-dashboards/*.yaml` (ConfigMaps), `ingress-grafana.yaml`.

**2. Install script** — new `scripts/monitoring-up.sh` (+ `monitoring-down.sh`), wrapped by `make mon-up`:

```bash
helm repo add prometheus-community https://prometheus-community.github.io/helm-charts
helm repo add grafana https://grafana.github.io/helm-charts
helm repo update
kubectl create namespace monitoring --dry-run=client -o yaml | kubectl apply -f -
kubectl label namespace monitoring pod-security.kubernetes.io/enforce=baseline --overwrite   # node-exporter/alloy exception (10b §1)

helm upgrade --install monitoring prometheus-community/kube-prometheus-stack \
  -n monitoring -f k8s/monitoring/values-kube-prometheus-stack.yaml --version "$KPS_VERSION"
helm upgrade --install loki grafana/loki   -n monitoring -f k8s/monitoring/values-loki.yaml  --version "$LOKI_VERSION"
helm upgrade --install alloy grafana/alloy -n monitoring -f k8s/monitoring/values-alloy.yaml --version "$ALLOY_VERSION"
helm upgrade --install tempo grafana/tempo -n monitoring -f k8s/monitoring/values-tempo.yaml --version "$TEMPO_VERSION"
helm upgrade --install postgres-exporter prometheus-community/prometheus-postgres-exporter \
  -n monitoring -f k8s/monitoring/values-postgres-exporter.yaml --version "$PGEXP_VERSION"

kubectl apply -f k8s/monitoring/servicemonitors.yaml \
              -f k8s/monitoring/prometheus-rules.yaml \
              -f k8s/monitoring/grafana-dashboards/ \
              -f k8s/monitoring/ingress-grafana.yaml
```

**Version pinning:** the `$*_VERSION` vars live in `.env` with defaults recorded in `lib.sh`; on first
install, run `helm search repo <chart>` and pin the CURRENT versions into `.env.example` + a comment in each
values file ("tested against chart X.Y.Z"). Never install unpinned — reproducibility is a thesis requirement.

**3. `values-kube-prometheus-stack.yaml`** — start from the Python repo's battle-tested file (it encodes four
diagnosed production failures) and adapt:

```yaml
kubelet:
  enabled: true
  serviceMonitor:                     # without this, container_* metrics are absent on minikube (cAdvisor over
    cAdvisor: true                    # kubelet HTTPS with self-signed cert + bearer token — all three needed)
    https: true
    insecureSkipVerify: true
    bearerTokenFile: /var/run/secrets/kubernetes.io/serviceaccount/token
prometheus:
  prometheusSpec:
    serviceMonitorSelectorNilUsesHelmValues: false   # or our app ServiceMonitors are ignored
    podMonitorSelectorNilUsesHelmValues: false
    ruleSelectorNilUsesHelmValues: false
    probeSelectorNilUsesHelmValues: false
    externalLabels: { cluster: forum-dotnet }        # stock dashboards filter by $cluster
    retention: 6h                                    # benchmark day, not long-term storage (§1)
    retentionSize: "2GB"
    enableFeatures: ["exemplar-storage"]             # metric→trace exemplar links (§7 below)
    resources:
      requests: { cpu: "100m", memory: "400Mi" }
      limits:   { cpu: "800m", memory: "768Mi" }
grafana:
  adminUser: admin
  adminPassword: admin                               # dev-only; grafana.forum.local is not exposed beyond the host
  defaultDashboardsEnabled: true
  resources:
    requests: { cpu: "100m", memory: "256Mi" }
    limits:   { cpu: "800m", memory: "512Mi" }       # 800m CPU: Grafana CFS-throttle restart lesson (Python repo, 2026-06-13)
  grafana.ini:
    database: { wal: true }                          # SQLITE_BUSY lesson
    dataproxy: { timeout: 20, dialTimeout: 10 }      # dead-datasource hang lesson
    analytics: { reporting_enabled: false, check_for_updates: false, check_for_plugin_updates: false }
    plugins: { preinstall_disabled: true }
  livenessProbe:  { initialDelaySeconds: 90, timeoutSeconds: 30, failureThreshold: 15, periodSeconds: 15 }
  readinessProbe: { initialDelaySeconds: 30, timeoutSeconds: 15, failureThreshold: 10, periodSeconds: 15 }
  sidecar:
    dashboards:  { enabled: true, label: grafana_dashboard, labelValue: "1", searchNamespace: ALL }
    datasources: { enabled: true, label: grafana_datasource, searchNamespace: ALL }
  additionalDataSources:
    - name: Loki
      type: loki
      access: proxy
      url: http://loki.monitoring.svc.cluster.local:3100
      jsonData:
        timeout: 20
        maxLines: 500
        derivedFields:                               # log line → Tempo trace (Serilog emits TraceId — 9a)
          - name: TraceID
            matcherRegex: '"TraceId":"(\w+)"'
            url: "$${__value.raw}"
            datasourceUid: tempo
    - name: Tempo
      type: tempo
      uid: tempo
      access: proxy
      url: http://tempo.monitoring.svc.cluster.local:3100
      jsonData:
        tracesToLogsV2:                              # trace → logs of the same pod ± 5s window
          datasourceUid: loki
          spanStartTimeShift: "-5s"
          spanEndTimeShift: "5s"
          filterByTraceID: true
alertmanager: { enabled: false }                     # §1: rules evaluate, nothing dispatches — recorded decision
nodeExporter: { enabled: true }
kube-state-metrics:
  resources: { requests: { cpu: "20m", memory: "64Mi" }, limits: { cpu: "100m", memory: "128Mi" } }
kubeEtcd: { enabled: false }                         # minikube control plane doesn't expose these — kill the
kubeControllerManager: { enabled: false }            # noisy "target down" alerts
kubeScheduler: { enabled: false }
kubeProxy: { enabled: false }
prometheusOperator:
  resources: { requests: { cpu: "50m", memory: "128Mi" }, limits: { cpu: "200m", memory: "256Mi" } }
```

Set the Prometheus datasource's exemplar link too (Grafana auto-provisions the Prometheus datasource; add
`exemplarTraceIdDestinations: [{ name: trace_id, datasourceUid: tempo }]` via the datasources sidecar if the
chart version doesn't expose it directly — one small ConfigMap with the `grafana_datasource` label).

**4. `values-loki.yaml`** (chart `grafana/loki`, SingleBinary):

```yaml
deploymentMode: SingleBinary
loki:
  auth_enabled: false
  commonConfig: { replication_factor: 1 }
  storage: { type: filesystem }
  schemaConfig:
    configs:
      - from: "2026-01-01"
        store: tsdb
        object_store: filesystem
        schema: v13
        index: { prefix: index_, period: 24h }
  limits_config:
    retention_period: 24h
    ingestion_rate_mb: 8              # k6 log-flood protection (Loki OOM lesson from the reference repo)
    ingestion_burst_size_mb: 16
    reject_old_samples: true
    reject_old_samples_max_age: 24h
  compactor: { retention_enabled: true, delete_request_store: filesystem }
singleBinary:
  replicas: 1
  persistence: { enabled: false }     # disposable demo storage — snapshots go to thesis/, not Loki
  resources:
    requests: { cpu: "150m", memory: "384Mi" }
    limits:   { cpu: "800m", memory: "768Mi" }
# turn off the chart's extra components (gateway, canary, chunks-cache default is memcached — all budget)
gateway: { enabled: false }
lokiCanary: { enabled: false }
test: { enabled: false }
chunksCache: { enabled: false }
resultsCache: { enabled: false }
```

*(Chart-version note: the `grafana/loki` chart's key layout shifts between majors — validate against the
pinned version with `helm template` before applying; the semantic targets above are what matter: single
binary, filesystem, 24 h retention, the two ingestion caps, no cache/canary/gateway sidecars.)*

**5. `values-alloy.yaml`** — DaemonSet shipping ALL pod logs with k8s labels, JSON-parsing our backend:

```yaml
alloy:
  configMap:
    content: |
      discovery.kubernetes "pods" { role = "pod" }
      discovery.relabel "pods" {
        targets = discovery.kubernetes.pods.targets
        rule { source_labels = ["__meta_kubernetes_namespace"],       target_label = "namespace" }
        rule { source_labels = ["__meta_kubernetes_pod_name"],        target_label = "pod" }
        rule { source_labels = ["__meta_kubernetes_pod_label_app"],   target_label = "app" }
        rule { source_labels = ["__meta_kubernetes_pod_container_name"], target_label = "container" }
      }
      loki.source.kubernetes "pods" {                       // tails via kubelet API — no hostPath mounts
        targets    = discovery.relabel.pods.output
        forward_to = [loki.write.default.receiver]
      }
      loki.write "default" { endpoint { url = "http://loki.monitoring.svc.cluster.local:3100/loki/api/v1/push" } }
  resources:
    requests: { cpu: "50m", memory: "128Mi" }
    limits:   { cpu: "200m", memory: "256Mi" }
```

Deliberate simplicity: no `stage.json` parsing in Alloy — the backend's CompactJsonFormatter lines stay whole,
Grafana's Loki UI json-parses at query time (`| json`), which keeps ingest cheap and non-JSON pods (postgres,
nginx) harmless.

**6. `values-tempo.yaml`:**

```yaml
tempo:
  reportingEnabled: false
  retention: 24h
  receivers: { otlp: { protocols: { grpc: { endpoint: "0.0.0.0:4317" } } } }
  resources:
    requests: { cpu: "100m", memory: "256Mi" }
    limits:   { cpu: "500m", memory: "512Mi" }
persistence: { enabled: false }
```

**7. `values-postgres-exporter.yaml`:** `config.datasource` from the existing `postgres-credentials` secret
(`host=postgres.forum-dotnet.svc.cluster.local user=forum dbname=forum_net sslmode=disable`, password via
`existingSecret`), `serviceMonitor: { enabled: true }`, resources 20m/100m + 32Mi/64Mi. Note: it consumes one
Postgres connection — already counted in the 10b pool math.

**8. `servicemonitors.yaml`** — three objects, all `namespace: forum-dotnet`-selecting from `monitoring`:

```yaml
apiVersion: monitoring.coreos.com/v1
kind: ServiceMonitor
metadata: { name: backend, namespace: monitoring, labels: { release: monitoring } }
spec:
  namespaceSelector: { matchNames: [forum-dotnet] }
  selector: { matchLabels: { app: backend } }          # ⚠ requires adding labels: { app: backend } to k8s/backend/service.yaml metadata — it has none today (G16)
  endpoints: [{ port: http, path: /metrics, interval: 15s, scrapeTimeout: 10s }]
---
# rabbitmq: port prometheus (15692), interval 15s   — same shape, selector app: rabbitmq (service labeled in 10b)
# minio:    port api (9000), path /minio/v2/metrics/cluster, interval 30s — MINIO_PROMETHEUS_AUTH_TYPE=public makes this tokenless
```

**Also required by this step (G16):** `k8s/backend/service.yaml` gains `labels: { app: backend }` and a
**named port** (`name: http`); same naming pass for rabbitmq/minio services (done in 10b manifests). The
`prometheus.io/*` pod annotations on the backend deployment stay as documentation but do nothing — say so in
a comment or delete them (delete; dead config lies).

**9. Dashboards** — ConfigMaps in `k8s/monitoring/grafana-dashboards/`, each labeled `grafana_dashboard: "1"`
(sidecar imports them). Build once in the Grafana UI, export JSON, commit. Panels + exact queries:

**(a) `cluster-overview.json`** *(supplements the stock kube-prometheus-stack dashboards, which stay enabled)*
- Node CPU %: `1 - avg(rate(node_cpu_seconds_total{mode="idle"}[5m]))`
- Node memory %: `1 - node_memory_MemAvailable_bytes / node_memory_MemTotal_bytes`
- Pod CPU (forum-dotnet): `sum by (pod) (rate(container_cpu_usage_seconds_total{namespace="forum-dotnet", container!=""}[2m]))`
- Pod memory working set: `sum by (pod) (container_memory_working_set_bytes{namespace="forum-dotnet", container!=""})`
- Pod restarts (15m): `sum by (pod) (increase(kube_pod_container_status_restarts_total{namespace=~"forum-dotnet|monitoring"}[15m]))`
- Pods not ready: `sum by (pod) (kube_pod_status_ready{condition="false", namespace="forum-dotnet"})`
- PVC usage %: `kubelet_volume_stats_used_bytes / kubelet_volume_stats_capacity_bytes{namespace="forum-dotnet"}`

**(b) `app-red.json`** — RED per route (OTel semconv names; backend job = `backend`):
- Request rate by route: `sum by (http_route) (rate(http_server_request_duration_seconds_count{job="backend"}[1m]))`
- Error rate %: `100 * sum(rate(http_server_request_duration_seconds_count{job="backend", http_response_status_code=~"5.."}[5m])) / clamp_min(sum(rate(http_server_request_duration_seconds_count{job="backend"}[5m])), 1)`
- p50/p95/p99 (repeat with 0.50/0.95/0.99, exemplars ON):
  `histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket{job="backend"}[5m])))`
- p95 by route (table): same with `by (le, http_route)`
- In-flight: `sum(http_server_active_requests{job="backend"})`
- .NET runtime row: GC heap `process_runtime_dotnet_gc_committed_memory_size_bytes`, Gen2 collections
  `rate(process_runtime_dotnet_gc_collections_count_total{generation="gen2"}[5m])`, thread-pool queue
  `process_runtime_dotnet_thread_pool_queue_length`, exceptions `rate(process_runtime_dotnet_exceptions_count_total[5m])`.
  *(Metric names from `OpenTelemetry.Instrumentation.Runtime`; verify exact spellings against `/metrics`
  once during implementation — pin whatever the installed package version emits.)*

**(c) `database.json`** (postgres-exporter):
- Connections vs cap: `sum(pg_stat_activity_count)` vs `pg_settings_max_connections` (threshold line at 100) —
  **the G8 money graph**: run the stress profile and watch it plateau at ~90.
- Per-state connections: `sum by (state) (pg_stat_activity_count)`
- TPS: `rate(pg_stat_database_xact_commit{datname="forum_net"}[1m])` + rollback rate
- Cache hit ratio: `pg_stat_database_blks_hit / (pg_stat_database_blks_hit + pg_stat_database_blks_read)`
- Lock waits: `pg_locks_count{mode=~".*Exclusive.*"}` ; Deadlocks: `rate(pg_stat_database_deadlocks[5m])`
- Row throughput: `rate(pg_stat_database_tup_fetched{datname="forum_net"}[1m])` / `tup_inserted` / `tup_updated`

**(d) `rabbitmq.json`** (built-in prometheus plugin, per-object metrics enabled in 10b):
- Ready messages by queue: `rabbitmq_queue_messages_ready` *(legend `{{queue}}` — shows `<module>.events`, `.retry`)*
- **Poison depth (the Phase 6 design's alarm bell):** `sum(rabbitmq_queue_messages_ready{queue=~".*\\.poison"})`
- Consumers per queue: `rabbitmq_queue_consumers`
- Publish/deliver/ack rates: `rate(rabbitmq_global_messages_received_total[1m])`, `rate(rabbitmq_global_messages_delivered_consume_auto_ack_total[1m]) + rate(rabbitmq_global_messages_delivered_consume_manual_ack_total[1m])`, `rate(rabbitmq_global_messages_acknowledged_total[1m])`
- Connections/channels: `rabbitmq_connections`, `rabbitmq_channels`

**(e) `minio.json`:**
- Bucket size / object count: `minio_bucket_usage_total_bytes{bucket="forum"}`, `minio_bucket_usage_object_total{bucket="forum"}`
- S3 request rate by API: `sum by (api) (rate(minio_s3_requests_total[5m]))` *(watch `PutObject` during the k6 upload scenario)*
- S3 errors: `sum(rate(minio_s3_requests_errors_total[5m]))` ; TTFB: `histogram_quantile(0.95, sum by (le) (rate(minio_s3_ttfb_seconds_distribution[5m])))` *(verify exact histogram name against the pinned MinIO release)*

**(f) `hpa-scaling.json`** (the thesis demo dashboard):
- Replicas over time: `kube_horizontalpodautoscaler_status_current_replicas{horizontalpodautoscaler="backend"}` + `..._desired_replicas` + `kube_horizontalpodautoscaler_spec_max_replicas` (max as dashed line)
- The driving signal: `sum(rate(container_cpu_usage_seconds_total{namespace="forum-dotnet", pod=~"backend-.*", container="backend"}[2m])) / sum(kube_pod_container_resource_requests{namespace="forum-dotnet", resource="cpu", pod=~"backend-.*"})` vs the 0.70 target line
- Per-pod CPU/memory small multiples; scaling events annotation query (Loki): `{namespace="forum-dotnet"} |= "Scaled up replica set backend"` — or `kube_event`-based if kube-state-metrics events are enabled; simplest: Grafana annotation on `changes(kube_horizontalpodautoscaler_status_current_replicas{horizontalpodautoscaler="backend"}[1m]) > 0`

**(g) `forum-business.json`** — entirely from 9a's Meter (this is why 9a lists exact names):
- Auth attempts by outcome: `sum by (outcome) (rate(forum_auth_attempts_total[5m]))`
- Content creation: `rate(forum_threads_created_total[5m])`, `rate(forum_comments_created_total[5m])`
- Reactions: `sum by (action) (rate(forum_reactions_total[5m]))`
- Outbox: published by module `sum by (module) (rate(forum_outbox_published_total[1m]))`; publish failures
  (stat, red when >0); **outbox lag p95:** `histogram_quantile(0.95, sum by (le) (rate(forum_outbox_lag_seconds_bucket[5m])))`
- Consumers by outcome: `sum by (module, outcome) (rate(forum_messaging_consumed_total[5m]))` *(a non-zero
  `poison`/`retry` series during a run = investigate before trusting numbers)*
- Realtime: `forum_ws_connections` (gauge), `forum_ws_subscriptions`, push rate `rate(forum_ws_pushes_total[1m])`
- **Missing-metric note (recorded):** comment counts in the feed are hard-coded 0 server-side (G22) and
  "active users" has no session concept — approximate active users as
  `count(count by (user) (…))` is NOT possible without user-tagged metrics (cardinality — forbidden in 9a);
  instead panel "authenticated request rate" via `sum(rate(http_server_request_duration_seconds_count{job="backend"}[5m]))`
  split by route group. If a true DAU metric is ever wanted, it needs a dedicated low-cardinality counter
  (e.g. `forum.sessions.refreshed`) — listed as optional, not built.

**10. `prometheus-rules.yaml`** — one `PrometheusRule` (label `release: monitoring`), thresholds:

| Alert | Expr (essence) | For | Sev |
|---|---|---|---|
| `BackendDown` | `(max(up{job="backend"}) or vector(0)) == 0` | 2m | critical |
| `BackendHighErrorRate` | 5xx% (query from dashboard (b)) `> 5` | 5m | warning |
| `BackendP95LatencyHigh` | p95 (dashboard (b)) `> 0.5` | 10m | warning |
| `BackendP99LatencyHigh` | p99 `> 2` | 10m | warning |
| `PodRestartLoop` | `increase(kube_pod_container_status_restarts_total{namespace=~"forum-dotnet\|monitoring"}[15m]) > 3` | 0m | critical |
| `PodMemoryNearLimit` | `container_memory_working_set_bytes / kube_pod_container_resource_limits{resource="memory"} > 0.9` | 10m | warning |
| `PostgresConnectionsNearMax` | `sum(pg_stat_activity_count) / pg_settings_max_connections > 0.85` | 5m | warning |
| `RabbitQueueBacklogGrowing` | `sum by (queue) (rabbitmq_queue_messages_ready{queue=~".*\\.events"}) > 100` | 10m | warning |
| `PoisonQueueNonEmpty` | `sum(rabbitmq_queue_messages_ready{queue=~".*\\.poison"}) > 0` | 5m | **critical** |
| `OutboxPublishFailures` | `rate(forum_outbox_publish_failures_total[5m]) > 0` | 5m | warning |
| `ReadinessFlapping` | `kube_pod_status_ready{condition="false", namespace="forum-dotnet"} == 1` | 5m | warning |
| `PVCAlmostFull` | `kubelet_volume_stats_used_bytes / kubelet_volume_stats_capacity_bytes > 0.85` | 10m | warning |
| `HPAMaxedOut` | `current_replicas >= spec_max_replicas` (kube-state-metrics) | 10m | info *(expected during stress — the annotation says so)* |

**11. Correlation story (write it into `infrastructure/monitoring/README.md`, replacing the stub):**
- **Request → everything:** `CorrelationIdMiddleware` puts `CorrelationId` on every log line; OTel gives the
  same request a `TraceId` which Serilog also emits (9a). The response's `X-Correlation-ID` header is the
  user-facing key; bus messages carry it (Phase 6) so consumer-side log lines correlate across pods:
  LogQL `{namespace="forum-dotnet", app="backend"} | json | CorrelationId="<id>"`.
- **Metrics → traces (exemplars):** Prometheus runs with `exemplar-storage`; the OTel .NET SDK attaches
  trace-based exemplars to histogram samples (default `TraceBasedExemplarFilter` when a sampled Activity is
  live). In dashboard (b)'s latency panels, enable exemplars → dots open the exact Tempo trace behind a p99 spike.
- **Traces → logs:** Tempo datasource `tracesToLogsV2` (config above) jumps to the pod's Loki stream ±5 s.
- **Logs → traces:** Loki derived field on `"TraceId"` (config above).
- **Verification recipe (goes in the runbook + DoD):** make one request via
  `curl -si https://forum.local/api/content/tags?query=a`, take `X-Correlation-ID`, find its log line in
  Grafana Explore, click TraceID → Tempo span tree (HTTP → Npgsql), click "Logs for this span" → back.

**12. Grafana ingress** (`ingress-grafana.yaml`, ns `monitoring`, host `grafana.forum.local`, tls
`forum-tls` copy in that namespace) — or skip ingress and rely on `make port-forward`; ship the ingress,
it's 15 lines and makes benchmark screenshots painless.

**Watch out.**
- The #1 silent failure is the ServiceMonitor not matching: label `release: monitoring` on the
  ServiceMonitor/Rule objects AND `serviceMonitorSelectorNilUsesHelmValues: false` — belt and suspenders
  (the values file sets the latter, keep both).
- The backend Service has NO labels today — without adding `app: backend` + named port `http`, the backend
  ServiceMonitor selects nothing and dashboard (b) is empty (G16).
- Loki/Alloy/Tempo chart layouts drift across majors — `helm template` against the PINNED version before apply;
  fix keys, not intent.
- cAdvisor scrape needs all three kubelet settings on minikube or (a) shows "No data" — copied from the
  reference repo which diagnosed it.
- Dashboards must use `$__rate_interval` where possible (hardcoded `[1m]`/`[5m]` above are the semantic
  targets; when exporting from the UI keep Grafana's interval macros).
- Don't enable Alertmanager "just in case" — the decision and its rationale are recorded; changing it costs RAM.
- monitoring ns PSS = baseline (node-exporter hostPath/hostNetwork, Alloy kubelet access). Do not try restricted.
- After `mon-up`, Prometheus needs ~1 min before targets appear; `make mon-check` (script step) waits and then
  asserts `up{job=~"backend|rabbitmq|minio|postgres-exporter"} == 1` via the API — build this check, humans forget.

**Definition of Done.** `make mon-up` from zero completes; Prometheus Targets page: backend ×N, rabbitmq,
minio, postgres-exporter, kubelet/cAdvisor, node-exporter, kube-state-metrics all UP; all seven dashboards
render non-empty against seeded + lightly-exercised cluster; the §11 correlation recipe round-trips
(metric exemplar → trace → log → trace); `PoisonQueueNonEmpty` fires when a hand-crafted malformed message is
parked (test it once via the dev-monitor's publish path or `rabbitmqadmin publish`); total monitoring-ns
memory requests ≤ the §1 budget lines (`kubectl -n monitoring describe quota` or `kubectl top pods -n monitoring`).

**START-OF-PHASE REMINDERS.**
- *Remember:* Helm ONLY for monitoring (values in `k8s/monitoring/`, versions pinned in `.env`); Loki chart
  (not deprecated loki-stack) + Alloy (not EOL Promtail) + Tempo direct-OTLP (no collector);
  `serviceMonitorSelectorNilUsesHelmValues: false` + `release: monitoring` labels; backend Service needs
  `app` label + named port first; exemplars on; Alertmanager stays off; dashboards are ConfigMaps with
  `grafana_dashboard: "1"`; every query above assumes the 9a metric names — if a panel is empty, check the
  name against `/metrics` before touching the query.

---

## Phase 10d — Performance & caching (the Redis verdict)

**Goal.** Decide Redis on evidence, not fashion; then spend the effort on the optimizations the benchmark can
actually see, and re-measure.

**Depends on.** 9c baseline numbers exist (optimize against data, never blind).

### The Redis evaluation (candidate by candidate, against the code as it exists)

| Candidate | Current mechanism | Verdict | Reasoning |
|---|---|---|---|
| Effective-permission cache | `forum_authz.effective_perm_cache` **in Postgres** (ADR 0004), recomputed synchronously on role/ACL change (Phase 6 recorded this as deliberate: security-sensitive, zero revocation window) | **No Redis** | Redis alongside would create a second cache tier with its own invalidation raciness — reintroducing exactly the revocation window the sync design eliminates. Redis *instead* would move authz's hot path off the database that owns the ACL source of truth and break the "resolved in SQL" thesis assumption (locked assumption #1/ADR 0004). Permission reads are a PK lookup on a cached table; Postgres does this in µs. |
| Feed/read caching | SQL views + keyset + indexes; SPA composes via fetch-then-patch + WS invalidation | **No Redis** | The realtime model (ADR 0010) is *fetch-then-patch*: server-side response caching would serve stale reads immediately after a WS notification told the client to re-fetch — self-defeating. The reads are already index-only keyset queries measured in ms. If the benchmark shows a hot-feed bottleneck, the fix is `reaction_counts`-style denormalization (already done) or Postgres tuning, not a cache tier. |
| Rate-limiter store | In-memory `PartitionedRateLimiter` per replica | **No Redis — accept and document** | Per-replica limits mean the effective global limit is `limit × replicas` (≤3×) — an upper-bound wobble, not a security hole (auth limiter still bounds credential stuffing at 30/min worst case vs 10 intended). .NET has no first-party distributed limiter store; pulling a community Redis limiter in for a thesis benchmark adds a network hop to EVERY request. Record the multiplier in the thesis; production note: "a shared store or ingress-level limiting would restore global semantics." |
| Refresh-token / logout-all lookups | Postgres, hashed token PK lookup | **No Redis** | One indexed PK read per refresh (≤1/15 min/user). Nothing to optimize. |
| WS ticket redeemed-jti replay cache | Per-replica memory; cross-replica replay bounded by the 30 s TTL (Phase 7 recorded this + named Redis as the hardening option) | **The only honest Redis use case — still No by default** | The exposure: a ticket replayed against a *different* replica within 30 s of minting. Prerequisite for exploitation is token theft in-flight (TLS) or XSS (the ticket never touches storage). Severity low, window 30 s, minikube runs 1–3 replicas. Adding a Redis pod + client + failure mode to close it is disproportionate. **Decision: don't deploy Redis.** The thesis writes this exact trade-off up (it reads as engineering maturity, not a gap). |

**If the decision is ever reversed** (e.g. the supervisor wants the distributed-cache chapter): single
`redis:7-alpine` Deployment (no HA — a cache that loses data on restart is fine by definition here; Sentinel
/Cluster on one node is cosplay), `--maxmemory 96mb --maxmemory-policy allkeys-lru`, restricted
securityContext, Service `redis:6379`, netpol backend→redis:6379, §1 budget line already reserved (128Mi),
`StackExchange.Redis` via CPM, and exactly ONE consumer: `ITicketReplayCache` behind the existing per-replica
implementation (strategy pattern, config-gated `Realtime:ReplayCache=Memory|Redis`).

### Code-level optimization audit (do these; each is measurable in 9c re-runs)

1. **Npgsql pool + `Max Auto Prepare`:** connection string (cluster secret) gains
   `Maximum Pool Size=30;Max Auto Prepare=20;Auto Prepare Min Usages=2` — the hot keyset/view queries become
   server-prepared after 2 uses; typically the single cheapest DB win in exactly this read-heavy shape. Verify
   no prepared-statement bloat via dashboard (c).
2. **Response compression: at INGRESS, not Kestrel** — `nginx.ingress.kubernetes.io/enable-brotli` is
   controller-scoped; simpler: enable gzip in the ingress-nginx addon ConfigMap (`use-gzip: "true"`,
   `gzip-types: application/json`). JSON feed pages compress ~5–10×; measure TTFB effect in (b). Keeps CPU off
   the measured backend pods (and B parity — B's SSR HTML is also compressed by its server).
3. **ThreadPool pre-warm** (`ThreadPool.SetMinThreads(64, …)` or `DOTNET_ThreadPool_MinThreads=64` env in
   configmap): Argon2id logins burst-block worker threads; min-thread starvation shows as p99 spikes in the
   first ramp step. Cheap, measurable on the demo profile's first staircase.
4. **Server GC check:** aspnet images default `DOTNET_gcServer=1` — verify via
   `process_runtime_dotnet_gc_committed_memory_size_bytes` behavior; with 512Mi limits consider
   `DOTNET_GCHeapHardLimitPercent=75` so GC respects the cgroup honestly (it reads cgroups by default on
   .NET 10 — verify, don't assume; one `kubectl exec`-less check via metrics).
5. **N+1 sweep (audit, expect clean):** reads are raw-ADO view queries (Phase 2 design) — verify the thread
   detail + comments + batch-reactions triple stays 3 round-trips total, and the k6 `open thread` action's
   Npgsql span count per trace == expected (Tempo makes this a 30-second check — that's why 9a wired DB spans).
6. **HPA interaction re-check after tuning:** faster requests → lower CPU → later scale-up; re-run `demo` and
   confirm the staircase still demonstrates (adjust VU steps if optimization "broke" the demo — a good
   thesis sentence).

**Definition of Done.** Redis decision table transcribed into the thesis notes; optimizations 1–4 applied +
`bench-run.sh demo` re-run archived as `thesis/results/A/<stamp>-postopt/`; before/after table produced
(req/s, p95, p99 per endpoint); no regression in any 9c threshold; decision recorded in this doc's changelog.

**START-OF-PHASE REMINDERS.**
- *Remember:* the verdict is **no Redis** — don't re-litigate it mid-session (the table above is the
  reasoning; reversal has an exact recipe if forced). Optimize only what dashboards can prove: pool+prepare,
  ingress gzip, min-threads, GC limits; verify with traces (span counts) not vibes; re-measure with the same
  bench-run procedure and archive both sides.

---

## Phase 10e — Optional Social module (go/no-go)

**Proportionality note:** explicitly low priority; this block is a decision framework + minimal scope, not a build plan.

**Go/no-go gate (ALL must be true, else skip permanently and keep the UI mock):**
1. Architecture B commits to building the same scope (friendships + text DMs) — otherwise it's unmeasured
   effort against the thesis timeline (REQUIREMENTS §1: OPTIONAL = "only if both sides implement it").
2. Phases 9a–10d are DONE and archived (benchmark numbers exist and are safe).
3. ≥ 2 full working weeks remain before the thesis freeze.

**If GO — minimal scope (one session-sized cut):** `Forum.Modules.Social` per the existing module recipe
(`forum_social` schema per DOMAIN-MODEL §6: `friendships` (requester/addressee, status pending|accepted,
unique pair), `direct_messages` (text only)); use cases: send/accept/remove friendship, send DM (gated on
accepted friendship — 404→403→422 as always), list conversation (keyset); events `FriendRequestSent/Accepted`,
`DirectMessageSent` via the standard outbox + a Realtime hub mapping for `DirectMessageSent` → recipient's
user view (the Phase 7 user-view plumbing already exists); frontend: replace the `/social` mock's local state
with the real endpoints (the UI is already built — that was the point of the mock); NO presence, NO
read-receipts, NO voice (OUT per spec). Seed: +5 000 friendships/+20 000 DMs only if B seeds them too.
k6: DM send/list action at ~3% weight, again only if B mirrors.

**Definition of Done (if GO).** Module boundary tests extended; DM E2E (friend → DM → live WS delivery to
recipient's user view); B parity confirmed in writing; benchmark re-run NOT required (Social endpoints are
measured separately per REQUIREMENTS §1).

---

## 11. Scripts & Makefile inventory (cross-phase)

Single source of truth for what §9–§10 add/change in `scripts/` + `Makefile`. Convention unchanged: bash +
`lib.sh` helpers, Makefile targets delegate via `bash scripts/<name>.sh`.

| Script | New/Extend | Phase | Behavior |
|---|---|---|---|
| `lib.sh` | extend | 10a/10c | `IMAGE_TAG=git-<sha>[-dirty]` default; `FRONTEND_IMAGE_NAME=forum-dotnet-web`; helm chart version vars with defaults; `helm` presence helper |
| `preflight.sh` | extend | 10a/10b/10c | add warn-only checks: `helm`, `mkcert`, `trivy`; check `MINIKUBE_MEMORY≥10240` + `MINIKUBE_CPUS≥6` against `.env` |
| `setup-minikube.sh` | extend | 10b | `--cni=calico`; new defaults `MINIKUBE_MEMORY=10240`, `MINIKUBE_CPUS=6`; print mkcert + hosts-entry instructions for the three hostnames |
| `deploy.sh` | extend | 10b | full order from 10b §11 incl. frontend image build, 4 secrets, rbac, rabbitmq/minio/bucket-job, `--seed` flag, unconditional netpols, SHA-tag set-image |
| `mkcert-tls.sh` | **new** | 10b | mkcert (or openssl fallback) → `forum-tls` secret in `forum-dotnet` + `monitoring` |
| `seed-test-data.sh` | rewrite | 9b | local (`dotnet run -- seed`) and `--cluster` (Job) modes; prints row counts |
| `monitoring-up.sh` / `monitoring-down.sh` | **new** | 10c | helm installs from §10c step 2 + apply servicemonitors/rules/dashboards/ingress; `mon-check` target-up assertion; down = `helm uninstall` ×5 + ns delete |
| `port-forward.sh` | **new** | 10c | `port-forward.sh grafana|prometheus|rabbitmq|minio-console` → stable local ports (3001/9090/15672/9001), prints URL + creds source |
| `tail-logs.sh` | **new** | 10b | `tail-logs.sh backend|frontend|postgres|rabbitmq|minio [-p]` → `kc logs -l app=$1 -f --tail=100` (`-p` previous) |
| `run-load-test.sh` | extend | 9c | profiles against `load/k6/main.js`; HPA/`kubectl top` sampler → `load/results/` |
| `bench-run.sh` | **new** | 9c | measured-run orchestrator (limiter raise/restore, warm-up, N repeats, Prometheus snapshots, `thesis/results/A/` archive) |
| `scan-image.sh` | **new** | 10a | trivy HIGH/CRITICAL on both images |
| `reset-db.sh` | extend | 9b | also delete `db-seed` Job |

Makefile additions (keep the `## help` format):
`web-image`, `scan`, `tls`, `seed` (`ARGS=--cluster`), `mon-up`, `mon-down`, `mon-check`, `pf` (`ARGS=grafana`),
`tail` (`ARGS=backend`), `bench` (`ARGS=demo`), plus `mk-deploy ARGS=--seed` passthrough. `urls` target grows
the https + grafana + minio-console lines.

---

## 12. Full bring-up runbook (cold machine → benchmark-ready)

The canonical order (each step idempotent; details in the phase blocks):

```bash
# 0. one-time host prep
#    %USERPROFILE%\.wslconfig → [wsl2] memory=12GB swap=4GB processors=6 → wsl --shutdown
make preflight                          # docker, kubectl, minikube, helm, dotnet, node, k6, mkcert, trivy

# 1. cluster (NOTE: switching to calico requires a fresh profile)
minikube delete -p forum                # only when migrating an old pre-calico profile
make mk-up                              # minikube start --cpus=6 --memory=10240 --cni=calico --addons=ingress,metrics-server
echo "$(minikube -p forum ip)  forum.local minio.forum.local grafana.forum.local" | sudo tee -a /etc/hosts
make tls                                # mkcert → forum-tls secrets

# 2. the app stack (images → secrets → infra → migrate → seed → app → ingress → netpols)
make mk-deploy ARGS=--seed              # scripts/deploy.sh order from Phase 10b §11
make pods                               # everything Ready; jobs Complete

# 3. monitoring
make mon-up && make mon-check           # helm ×5 + dashboards; all targets UP
make pf ARGS=grafana                    # or https://grafana.forum.local (admin/admin)

# 4. smoke the golden path (browser): register→login→feed→thread→comment→like→upload avatar→LIVE pill
make load ARGS=smoke                    # k6 sanity through ingress

# 5. measured runs (repeatable)
make bench ARGS=demo                    # warm-up + 3 repeats + archive → thesis/results/A/<stamp>/
make bench ARGS=stress

# 6. after 10d optimizations
make bench ARGS=demo                    # → thesis/results/A/<stamp>-postopt/

# teardown / lifecycle
make mon-down                           # reclaim ~3 GiB when not benchmarking
make mk-down ARGS=--stop                # freeze the VM between sessions
```

Update `docs/runbooks/wsl-minikube-setup.md` §3 to reference this sequence and delete its stale
"NetworkPolicies stay off by default" paragraph once 10b lands (the policies become mandatory + enforced).

---

## 13. CLAUDE.md updates needed

Apply when the FIRST block of this plan starts (so future sessions route here), then keep the per-phase
"Current state" entries updating as blocks complete.

**1. In `## Authoritative design docs`, add after the IMPLEMENTATION-PLAN.md line:**

```markdown
- **`docs/architecture/PHASE-9-10-ENTERPRISE-PLAN.md`** — SUPERSEDES the Phase 9/10 blocks of
  IMPLEMENTATION-PLAN.md: enterprise k8s (PSS/RBAC/NetworkPolicies+calico/TLS), monitoring stack
  (kube-prometheus-stack+Loki+Alloy+Tempo via Helm, exact values/dashboards/alerts), deterministic seed,
  k6 demo/stress + benchmark runbook, 12 GiB resource contract, Redis verdict (no), gap register G1–G22.
  Blocks: 9a observability code · 9b seed · 9c load/bench · 10a images · 10b k8s core · 10c monitoring ·
  10d perf · 10e social go/no-go. Re-read the relevant block before coding, same as IMPLEMENTATION-PLAN phases.
```

**2. Replace the `## Build order (next work)` final line** (`Phase 9 Seed+benchmark... → 10 k8s deploy...`) with:

```markdown
9 Seed+benchmark+observability → 10 k8s deploy+hardening — BOTH now specified in
`docs/architecture/PHASE-9-10-ENTERPRISE-PLAN.md` (execution order 9a→9b→10a→10b→10c→9c→10d→[10e]);
the Phase 9/10 blocks in IMPLEMENTATION-PLAN.md are superseded.
```

**3. In `## Current state`, replace the trailing "Next: Phase 9 …" sentence with:**

```markdown
**Next: PHASE-9-10-ENTERPRISE-PLAN.md, block 9a.** Known-broken-by-design until its blocks land: `make
mk-deploy` cannot produce a ready backend (no rabbitmq/minio manifests, guest-loopback creds, presign host,
CORS origins — gap register G1–G22 in that plan). Phase 5 Social remains OPTIONAL (10e go/no-go gate).
```

**4. As each block completes,** append a one-paragraph entry to `## Current state` in the established style
(what shipped, key decisions, verified-green line), and tick the block in this plan's changelog below.

---

## Appendix A — literal file payloads

Where a phase block above summarized a file, this appendix carries the full intended content, so a session
can copy-adapt instead of designing. Any conflict: the phase block's *reasoning* wins; fix the payload.

### A.1 `k8s/backend/deployment.yaml` — final form after 10a + 10b

```yaml
apiVersion: apps/v1
kind: Deployment
metadata: { name: backend, namespace: forum-dotnet }
spec:
  replicas: 2
  strategy:
    type: RollingUpdate
    rollingUpdate: { maxSurge: 1, maxUnavailable: 0 }     # never dip below current capacity during rollout (G17)
  selector: { matchLabels: { app: backend } }
  template:
    metadata:
      labels: { app: backend }
    spec:
      serviceAccountName: backend
      automountServiceAccountToken: false                  # backend never talks to the k8s API (G12)
      terminationGracePeriodSeconds: 40                    # preStop 5s + HostOptions ShutdownTimeout 25s + margin
      securityContext:
        runAsNonRoot: true                                 # image user is 'app' (uid 1654, chiseled) — no runAsUser pin
        seccompProfile: { type: RuntimeDefault }           # required by PSS restricted
      containers:
        - name: backend
          image: forum-dotnet-api:local                    # placeholder; deploy.sh pins the git-<sha> tag via set-image
          imagePullPolicy: Never
          ports: [{ containerPort: 8080, name: http }]
          envFrom:
            - { configMapRef: { name: backend-config } }
            - { secretRef: { name: backend-secrets } }     # Jwt__SigningKey (G19 guard requires it in Production)
            - { secretRef: { name: rabbitmq-credentials } }
            - { secretRef: { name: minio-credentials } }
          env:
            - name: ConnectionStrings__Forum
              valueFrom: { secretKeyRef: { name: postgres-credentials, key: CONNECTION_STRING } }
          lifecycle:
            preStop: { sleep: { seconds: 5 } }             # endpoint propagation before SIGTERM (chiseled: no shell for exec)
          resources:
            requests: { cpu: "150m", memory: "256Mi" }
            limits:   { cpu: "750m", memory: "512Mi" }
          readinessProbe: { httpGet: { path: /health/ready, port: 8080 }, initialDelaySeconds: 5, periodSeconds: 10 }
          livenessProbe:  { httpGet: { path: /health/live,  port: 8080 }, initialDelaySeconds: 10, periodSeconds: 20 }
          securityContext:
            allowPrivilegeEscalation: false
            readOnlyRootFilesystem: true
            capabilities: { drop: ["ALL"] }
          volumeMounts: [{ name: tmp, mountPath: /tmp }]   # ro-rootfs insurance for BCL temp paths
      volumes: [{ name: tmp, emptyDir: {} }]
```

`k8s/backend/service.yaml` — final form (labels + named port required by the ServiceMonitor, G16):

```yaml
apiVersion: v1
kind: Service
metadata:
  name: backend
  namespace: forum-dotnet
  labels: { app: backend }
spec:
  selector: { app: backend }
  ports: [{ name: http, port: 80, targetPort: 8080 }]
```

`k8s/backend/configmap.yaml` — final form:

```yaml
apiVersion: v1
kind: ConfigMap
metadata: { name: backend-config, namespace: forum-dotnet }
data:
  ASPNETCORE_ENVIRONMENT: "Production"
  Otlp__Endpoint: "http://tempo.monitoring.svc.cluster.local:4317"   # direct to Tempo — no otel-collector (G11)
  RabbitMq__Host: "rabbitmq"
  Storage__Endpoint: "minio:9000"
  Storage__PublicEndpoint: "minio.forum.local"                       # presign host the BROWSER can reach (G5)
  Storage__PublicUseSsl: "false"                                     # flip to "true" once forum-tls is live
  Cors__AllowedOrigins__0: "http://forum.local"                      # without these the deployed SPA is CORS-blocked
  Cors__AllowedOrigins__1: "https://forum.local"
  ForwardedHeaders__KnownNetworks__0: "10.244.0.0/16"                # verify pod CIDR under calico (9a made it config)
  DOTNET_ThreadPool_MinThreads: "64"                                 # 10d #3 — Argon2id burst headroom
  # RateLimiting__Global__PermitLimit intentionally NOT set here: production posture (100/min/IP) is the
  # default; bench-run.sh raises it per measured run and restores (9c).
```

### A.2 `k8s/backend/seed-job.yaml`

```yaml
# Deterministic benchmark seed (Phase 9b). Run AFTER db-migrate completes, on a FRESH database —
# the seeder aborts on non-empty data unless forced (never force from k8s).
apiVersion: batch/v1
kind: Job
metadata: { name: db-seed, namespace: forum-dotnet }
spec:
  backoffLimit: 0                     # a failed seed must be inspected, not retried into a half-seeded DB
  activeDeadlineSeconds: 600
  template:
    metadata:
      labels: { app: backend }        # postgres NetworkPolicy admits app=backend (10b §10)
    spec:
      restartPolicy: Never
      serviceAccountName: backend
      automountServiceAccountToken: false
      securityContext: { runAsNonRoot: true, seccompProfile: { type: RuntimeDefault } }
      containers:
        - name: seed
          image: forum-dotnet-api:local
          imagePullPolicy: Never
          args: ["seed"]
          envFrom:
            - { configMapRef: { name: backend-config } }
            - { secretRef: { name: backend-secrets } }
            - { secretRef: { name: rabbitmq-credentials } }
            - { secretRef: { name: minio-credentials } }
          env:
            - name: ConnectionStrings__Forum
              valueFrom: { secretKeyRef: { name: postgres-credentials, key: CONNECTION_STRING } }
          resources:
            requests: { cpu: "250m", memory: "256Mi" }
            limits:   { cpu: "500m", memory: "512Mi" }
          securityContext: { allowPrivilegeEscalation: false, readOnlyRootFilesystem: true, capabilities: { drop: ["ALL"] } }
          volumeMounts: [{ name: tmp, mountPath: /tmp }]
      volumes: [{ name: tmp, emptyDir: {} }]
```

*(`migration-job.yaml` gets the same additions: `labels: { app: backend }`, serviceAccount, securityContext,
resources, `activeDeadlineSeconds: 600` — its `backoffLimit: 3` stays, migrations are idempotent.)*

### A.3 `k8s/rbac.yaml`

```yaml
# Least privilege here means NO API access at all: none of these workloads talks to the Kubernetes API,
# so each gets a dedicated ServiceAccount with token automount disabled and ZERO Roles/RoleBindings.
# (An empty Role would be security theater; absence of a token is strictly stronger.)
# Components that DO need API access (prometheus-operator, kube-state-metrics, alloy) ship their own
# RBAC via their Helm charts in the monitoring namespace.
apiVersion: v1
kind: ServiceAccount
metadata: { name: backend, namespace: forum-dotnet }
automountServiceAccountToken: false
---
apiVersion: v1
kind: ServiceAccount
metadata: { name: frontend, namespace: forum-dotnet }
automountServiceAccountToken: false
---
apiVersion: v1
kind: ServiceAccount
metadata: { name: postgres, namespace: forum-dotnet }
automountServiceAccountToken: false
---
apiVersion: v1
kind: ServiceAccount
metadata: { name: rabbitmq, namespace: forum-dotnet }
automountServiceAccountToken: false
---
apiVersion: v1
kind: ServiceAccount
metadata: { name: minio, namespace: forum-dotnet }
automountServiceAccountToken: false
```

### A.4 `k8s/frontend/deployment.yaml` + `service.yaml`

```yaml
apiVersion: apps/v1
kind: Deployment
metadata: { name: frontend, namespace: forum-dotnet }
spec:
  replicas: 1
  selector: { matchLabels: { app: frontend } }
  template:
    metadata: { labels: { app: frontend } }
    spec:
      serviceAccountName: frontend
      automountServiceAccountToken: false
      securityContext: { runAsNonRoot: true, seccompProfile: { type: RuntimeDefault } }   # image runs as 'node'
      containers:
        - name: frontend
          image: forum-dotnet-web:local          # deploy.sh pins git-<sha>; built with NEXT_PUBLIC_API_URL=http(s)://forum.local (HOST ONLY — no /api path! see 10a step 2)
          imagePullPolicy: Never
          ports: [{ containerPort: 3000, name: http }]
          readinessProbe: { httpGet: { path: /, port: 3000 }, initialDelaySeconds: 5, periodSeconds: 10 }
          livenessProbe:  { httpGet: { path: /, port: 3000 }, initialDelaySeconds: 15, periodSeconds: 20 }
          resources:
            requests: { cpu: "50m", memory: "128Mi" }
            limits:   { cpu: "300m", memory: "256Mi" }
          securityContext:
            allowPrivilegeEscalation: false
            readOnlyRootFilesystem: true
            capabilities: { drop: ["ALL"] }
          volumeMounts:
            - { name: next-cache, mountPath: /app/.next/cache }   # only write path Next needs under ro-rootfs
            - { name: tmp, mountPath: /tmp }
      volumes:
        - { name: next-cache, emptyDir: {} }
        - { name: tmp, emptyDir: {} }
---
apiVersion: v1
kind: Service
metadata:
  name: frontend
  namespace: forum-dotnet
  labels: { app: frontend }
spec:
  selector: { app: frontend }
  ports: [{ name: http, port: 80, targetPort: 3000 }]
```

### A.5 `k8s/minio/ingress.yaml` + `create-bucket-job.yaml`

```yaml
# Separate Ingress object so the body-size annotation stays scoped to presigned uploads (5 MiB cap + headroom).
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: minio-presigned
  namespace: forum-dotnet
  annotations:
    nginx.ingress.kubernetes.io/proxy-body-size: "10m"
    nginx.ingress.kubernetes.io/ssl-redirect: "false"      # presigned URLs are generated with the scheme the
                                                           # backend was told (Storage__PublicUseSsl) — keep in sync
spec:
  tls: [{ hosts: [minio.forum.local], secretName: forum-tls }]
  rules:
    - host: minio.forum.local
      http:
        paths:
          - { path: /, pathType: Prefix, backend: { service: { name: minio, port: { number: 9000 } } } }
---
apiVersion: batch/v1
kind: Job
metadata: { name: minio-create-bucket, namespace: forum-dotnet }
spec:
  backoffLimit: 3
  activeDeadlineSeconds: 300
  template:
    metadata: { labels: { app: backend } }   # reuse the backend allow-path toward minio:9000 (40-minio-allow)
    spec:
      restartPolicy: Never
      securityContext: { runAsNonRoot: true, runAsUser: 1000, seccompProfile: { type: RuntimeDefault } }
      containers:
        - name: mc
          image: minio/mc:latest             # pin a release tag at implementation time
          command: ["/bin/sh", "-c"]
          args:
            - mc alias set local http://minio:9000 "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD" && mc mb -p local/forum
          envFrom: [{ secretRef: { name: minio-credentials } }]
          resources:
            requests: { cpu: "50m", memory: "64Mi" }
            limits:   { cpu: "200m", memory: "128Mi" }
          securityContext: { allowPrivilegeEscalation: false, capabilities: { drop: ["ALL"] } }
```

### A.6 `k8s/monitoring/servicemonitors.yaml` — all three, complete

```yaml
apiVersion: monitoring.coreos.com/v1
kind: ServiceMonitor
metadata: { name: forum-backend, namespace: monitoring, labels: { release: monitoring } }
spec:
  namespaceSelector: { matchNames: [forum-dotnet] }
  selector: { matchLabels: { app: backend } }
  endpoints: [{ port: http, path: /metrics, interval: 15s, scrapeTimeout: 10s }]
---
apiVersion: monitoring.coreos.com/v1
kind: ServiceMonitor
metadata: { name: forum-rabbitmq, namespace: monitoring, labels: { release: monitoring } }
spec:
  namespaceSelector: { matchNames: [forum-dotnet] }
  selector: { matchLabels: { app: rabbitmq } }
  endpoints: [{ port: prometheus, path: /metrics, interval: 15s, scrapeTimeout: 10s }]
---
apiVersion: monitoring.coreos.com/v1
kind: ServiceMonitor
metadata: { name: forum-minio, namespace: monitoring, labels: { release: monitoring } }
spec:
  namespaceSelector: { matchNames: [forum-dotnet] }
  selector: { matchLabels: { app: minio } }
  endpoints: [{ port: api, path: /minio/v2/metrics/cluster, interval: 30s, scrapeTimeout: 10s }]
```

*(Requires: rabbitmq Service labeled `app: rabbitmq` with port named `prometheus`; minio Service labeled
`app: minio` with port named `api` — both specified in 10b.)*

### A.7 `k8s/monitoring/prometheus-rules.yaml` — complete

```yaml
apiVersion: monitoring.coreos.com/v1
kind: PrometheusRule
metadata:
  name: forum-rules
  namespace: monitoring
  labels: { release: monitoring }
spec:
  groups:
    - name: forum.availability
      rules:
        - alert: BackendDown
          expr: (max(up{job=~".*backend.*"}) or vector(0)) == 0
          for: 2m
          labels: { severity: critical }
          annotations: { summary: "No healthy backend scrape targets for 2m" }
        - alert: ReadinessFlapping
          expr: kube_pod_status_ready{condition="false", namespace="forum-dotnet"} == 1
          for: 5m
          labels: { severity: warning }
          annotations: { summary: "Pod {{ $labels.pod }} not ready for 5m" }
        - alert: PodRestartLoop
          expr: increase(kube_pod_container_status_restarts_total{namespace=~"forum-dotnet|monitoring"}[15m]) > 3
          labels: { severity: critical }
          annotations: { summary: "{{ $labels.pod }} restarted >3× in 15m (crashloop?)" }
    - name: forum.latency-errors
      rules:
        - alert: BackendHighErrorRate
          expr: |
            100 * sum(rate(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[5m]))
              / clamp_min(sum(rate(http_server_request_duration_seconds_count[5m])), 1) > 5
          for: 5m
          labels: { severity: warning }
          annotations: { summary: "Backend 5xx rate above 5% for 5m" }
        - alert: BackendP95LatencyHigh
          expr: histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket[5m]))) > 0.5
          for: 10m
          labels: { severity: warning }
          annotations: { summary: "Backend p95 above 500ms for 10m" }
        - alert: BackendP99LatencyHigh
          expr: histogram_quantile(0.99, sum by (le) (rate(http_server_request_duration_seconds_bucket[5m]))) > 2
          for: 10m
          labels: { severity: warning }
          annotations: { summary: "Backend p99 above 2s for 10m" }
    - name: forum.resources
      rules:
        - alert: PodMemoryNearLimit
          expr: |
            max by (namespace, pod, container) (container_memory_working_set_bytes{container!=""})
              / clamp_min(max by (namespace, pod, container) (kube_pod_container_resource_limits{resource="memory"}), 1) > 0.9
          for: 10m
          labels: { severity: warning }
          annotations: { summary: "{{ $labels.pod }}/{{ $labels.container }} above 90% of memory limit" }
        - alert: PVCAlmostFull
          expr: kubelet_volume_stats_used_bytes / kubelet_volume_stats_capacity_bytes > 0.85
          for: 10m
          labels: { severity: warning }
          annotations: { summary: "PVC {{ $labels.persistentvolumeclaim }} above 85%" }
        - alert: HPAMaxedOut
          expr: |
            kube_horizontalpodautoscaler_status_current_replicas{horizontalpodautoscaler="backend"}
              >= kube_horizontalpodautoscaler_spec_max_replicas{horizontalpodautoscaler="backend"}
          for: 10m
          labels: { severity: info }
          annotations: { summary: "Backend at max replicas (expected during stress profile)" }
    - name: forum.messaging
      rules:
        - alert: PoisonQueueNonEmpty
          expr: sum(rabbitmq_queue_messages_ready{queue=~".*\\.poison"}) > 0
          for: 5m
          labels: { severity: critical }
          annotations: { summary: "Messages parked in a poison queue — investigate before trusting benchmark data" }
        - alert: RabbitQueueBacklogGrowing
          expr: sum by (queue) (rabbitmq_queue_messages_ready{queue=~".*\\.events"}) > 100
          for: 10m
          labels: { severity: warning }
          annotations: { summary: "Queue {{ $labels.queue }} backlog >100 for 10m (consumer starving?)" }
        - alert: OutboxPublishFailures
          expr: rate(forum_outbox_publish_failures_total[5m]) > 0
          for: 5m
          labels: { severity: warning }
          annotations: { summary: "Outbox relay failing to publish (module {{ $labels.module }})" }
    - name: forum.database
      rules:
        - alert: PostgresConnectionsNearMax
          expr: sum(pg_stat_activity_count) / max(pg_settings_max_connections) > 0.85
          for: 5m
          labels: { severity: warning }
          annotations: { summary: "Postgres above 85% of max_connections — check pool math (G8)" }
```

### A.8 `load/k6/main.js` — structural skeleton (implement fully in 9c)

```javascript
// k6 profiles for forum-dotnet (Architecture A). PROFILE=smoke|demo|stress, BASE_URL=http(s)://forum.local
// Design notes live in docs/architecture/PHASE-9-10-ENTERPRISE-PLAN.md §9c — read before editing weights.
import http from "k6/http";
import ws from "k6/ws";
import { check, sleep } from "k6";
import { Rate, Counter } from "k6/metrics";
import { login, authHeaders, pickSeededUser } from "./lib/api.js";
import { TINY_PNG } from "./lib/assets.js";        // Uint8Array of a valid 1x1 PNG (magic bytes matter: ImageProbe)

const BASE_URL = __ENV.BASE_URL || "http://forum.local";
const WS_URL = (__ENV.WS_URL || BASE_URL.replace(/^http/, "ws")) + "/api/realtime/ws";
const PROFILE = (__ENV.PROFILE || "smoke").toLowerCase();

const errorRate = new Rate("errors");
const wsNotifications = new Counter("forum_ws_notifications");

const PROFILES = {
  smoke:  { http: [{ duration: "60s", target: 5 }], ws: 0,
            thresholds: { http_req_duration: ["p(95)<500"], errors: ["rate<0.01"] } },
  demo:   { http: [ { duration: "1m", target: 10 }, { duration: "30s", target: 40 }, { duration: "2m", target: 40 },
                    { duration: "30s", target: 80 }, { duration: "2m", target: 80 }, { duration: "1m", target: 0 } ],
            ws: 20,
            thresholds: { http_req_duration: ["p(95)<800"], errors: ["rate<0.02"] } },
  stress: { http: [ { duration: "30s", target: 50 }, { duration: "1m", target: 50 }, { duration: "30s", target: 100 },
                    { duration: "1m", target: 100 }, { duration: "30s", target: 150 }, { duration: "2m", target: 150 },
                    { duration: "1m", target: 0 } ],
            ws: 40,
            thresholds: { http_req_duration: ["p(95)<2000"], errors: ["rate<0.05"] } },  // informational — stress documents limits
};
const profile = PROFILES[PROFILE] || PROFILES.smoke;

export const options = {
  scenarios: {
    browse: { executor: "ramping-vus", startVUs: 0, stages: profile.http, gracefulRampDown: "20s", exec: "browse" },
    ...(profile.ws > 0 && {
      realtime: { executor: "constant-vus", vus: profile.ws, duration: totalDuration(profile.http), exec: "realtime" },
    }),
  },
  thresholds: {
    ...profile.thresholds,
    // per-endpoint breakdown for the thesis tables (loose caps — the split is the point)
    "http_req_duration{endpoint:feed}": ["p(95)<3000"],
    "http_req_duration{endpoint:thread_open}": ["p(95)<3000"],
    "http_req_duration{endpoint:search}": ["p(95)<3000"],
    "http_req_duration{endpoint:write_comment}": ["p(95)<3000"],
    "http_req_duration{endpoint:upload}": ["p(95)<5000"],
  },
  summaryTrendStats: ["avg", "min", "med", "max", "p(90)", "p(95)", "p(99)", "count"],
};

export function setup() {
  // Collect seeded ids for realistic navigation + login a 200-user token pool (staggered ≤5 rps —
  // the auth rate-limit stays at production posture; only the GLOBAL limit is raised by bench-run.sh).
  // Returns { categories: [...], threads: [...], users: [{id, token}], corpusWords: [...] }.
}

export function browse(data) {
  const r = Math.random() * 100;
  if      (r < 30) feedAction(data);            // GET threads?categoryId (+30% follow nextCursor)
  else if (r < 50) threadOpenAction(data);      // detail + comments + reactions/batch — the SPA's real 3-call pattern
  else if (r < 60) searchAction(data);
  else if (r < 68) tagsAction(data);
  else if (r < 78) writeCommentAction(data);    // authenticated
  else if (r < 83) writeThreadAction(data);     // authenticated, 1–3 tags
  else if (r < 95) reactionToggleAction(data);  // authenticated PUT/DELETE
  else if (r < 98) uploadAction(data);          // initiate → presigned PUT (TINY_PNG) → commit
  else             profileStatsAction(data);
  sleep(0.3 + Math.random() * 0.4);             // think time — same distribution as B's harness
}

export function realtime(data) {
  const user = pickSeededUser(data);
  const ticket = http.post(`${BASE_URL}/api/realtime/ticket`, null, authHeaders(user)).json().ticket;
  const category = data.categories[__VU % data.categories.length];
  ws.connect(`${WS_URL}?ticket=${ticket}`, {}, (socket) => {
    socket.on("open", () => socket.send(JSON.stringify({ action: "subscribe", view: "category", id: category })));
    socket.on("message", (msg) => {
      const m = JSON.parse(msg);
      if (m.entity) wsNotifications.add(1);
      if (m.type === "subscribed") check(m, { "ws subscribed": () => true });
    });
    socket.setTimeout(() => socket.close(), remainingDurationMs());
  });
}

export function handleSummary(data) {
  // stdout table + ===K6_SUMMARY_JSON_BEGIN===/END=== block consumed by scripts/run-load-test.sh (9c).
}
```

### A.9 `scripts/bench-run.sh` — behavioral skeleton

```bash
#!/usr/bin/env bash
# Measured benchmark orchestrator (Phase 9c). Usage: bench-run.sh [demo|stress] [repeats=3]
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"; load_env
require_cmd kubectl; require_cmd k6; require_cmd curl; require_cmd python3
PROFILE="${1:-demo}"; REPEATS="${2:-3}"; STAMP="$(date +%Y-%m-%d_%H-%M)"
OUT="$REPO_ROOT/thesis/results/A/$STAMP-$PROFILE"; mkdir -p "$OUT"

step "Preflight"
kc get deploy backend >/dev/null || die "cluster not deployed"
kubectl get ns monitoring >/dev/null || die "monitoring stack not installed (make mon-up)"
# seed sentinel: refuse to benchmark an unseeded DB
[[ "$(kc exec postgres-0 -- psql -U forum -d forum_net -tAc 'select count(*) from forum_identity.users')" -ge 2000 ]] \
  || die "database not seeded (make seed ARGS=--cluster)"

step "Raising global rate limit for the measured run (recorded in meta.json)"
kc set env deployment/backend RateLimiting__Global__PermitLimit=1000000
kc rollout status deployment/backend --timeout=180s

step "Recording run metadata"
python3 - > "$OUT/meta.json" <<EOF
import json, subprocess as s
print(json.dumps({
  "profile": "$PROFILE", "repeats": $REPEATS, "stamp": "$STAMP",
  "git_sha": s.check_output(["git","rev-parse","HEAD"]).decode().strip(),
  "image_tag": "$IMAGE_TAG",
  "rate_limit_override": 1000000,
  "minikube": {"cpus": "$MINIKUBE_CPUS", "memory_mb": "$MINIKUBE_MEMORY"},
}))
EOF

step "Warm-up (discarded)"
bash "$LIB_DIR/run-load-test.sh" smoke "https://$INGRESS_HOST" || true

for i in $(seq 1 "$REPEATS"); do
  step "Measured run $i/$REPEATS ($PROFILE)"
  RESULTS_DIR="$OUT/run-$i" bash "$LIB_DIR/run-load-test.sh" "$PROFILE" "https://$INGRESS_HOST"
  step "Prometheus snapshots for run $i"
  # port-forward prometheus, then query_range for the §10c key series over the run window into run-$i/prom/*.json
  # (request rate, p95, p99, error rate, backend pod CPU/mem, HPA replicas, pg connections, queue depth)
  sleep 120   # cool-down
done

step "Restoring production rate-limit posture"
kc set env deployment/backend RateLimiting__Global__PermitLimit-
kc rollout status deployment/backend --timeout=180s

step "Aggregating"
# python3 one-liner: mean±stddev of req/s and p95 across run-*/summary.json → $OUT/aggregate.json + stdout table
ok "Archived to $OUT"
```

### A.10 `.env.example` additions

```bash
# --- Phase 9/10 additions ----------------------------------------------------
MINIKUBE_CPUS=6                 # was 4 — benchmark sizing assumption (§1); correct here if the host differs
MINIKUBE_MEMORY=10240           # was 8192 — 10 GiB VM inside the ≤12 GiB contract (§1)
FRONTEND_IMAGE_NAME=forum-dotnet-web
APPLY_NETWORK_POLICIES=true     # was false — allow-rules exist and calico enforces them (Phase 10b)
# Helm chart versions — pin on first install (helm search repo <name>); never deploy unpinned.
KPS_VERSION=                    # kube-prometheus-stack
LOKI_VERSION=
ALLOY_VERSION=
TEMPO_VERSION=
PGEXP_VERSION=                  # prometheus-postgres-exporter
```

---

## Change log

- **2026-07-07** — initial version: gap register G1–G22 from repo inspection; 12 GiB contract; blocks
  9a–9c, 10a–10e; scripts inventory; runbook; informed by Python-Forum-API's k8s/monitoring/k6 experience
  (adopted: values-file failure lessons, netpol shapes, profile staircase, HPA sampler; improved: Loki/Alloy
  instead of loki-stack/Promtail, ingress-scoped MinIO exposure instead of 0.0.0.0/0 + NodePort, realistic
  write-path k6 mix, enforced CNI, PSS, tokenless ServiceAccounts, pool math, chiseled images).



