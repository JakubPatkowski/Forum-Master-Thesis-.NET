# Thesis — Revised Sections Draft (working material, English)

_Written 2026-07-18. Redlined replacement drafts for the manuscript sections that no longer
match reality, per `AB-THESIS-GAP-ANALYSIS.md`. **The registered title, the abstract's
framing, §1.1–§1.2, §2 (related work), §3.1 (research design), and §3.6 (statistical
analysis) are deliberately untouched** — they remain valid. This file is working material in
English; the manuscript's final prose (and any Polish translation) is the authors' job._

_Change-tracking convention: each section starts with **WHAT CHANGED / WHY** so edits can be
transplanted into the manuscript deliberately, not wholesale._

---

## Revised §1.3 — Hypotheses

**WHAT CHANGED:** H1's mechanism clause (both desktop apps now share the same Tauri v2 shell,
so "elimination of a heavy JavaScript runtime" is no longer the differentiator — the
client-side application runtime is); H2 gains an explicit ceiling definition over user
journeys; H3's CI-pipeline clause is replaced by local developer-loop timings (cross-platform
SaaS CI runners are not "identical controlled infrastructure"). Substance of all three
hypotheses preserved.

> Three primary hypotheses guide the investigation:
>
> **H1 (Resource Efficiency).** Architecture B (unified monolith) will exhibit a significantly
> smaller memory footprint, CPU consumption, and delivered-artifact size than Architecture A
> (decoupled stack) under equivalent workloads — on the server tier per served user journey,
> and on the desktop tier, where both applications are packaged with the same native
> WebView shell (Tauri v2), attributable to the elimination of the client-side application
> runtime (framework bundle, hydration, and client-side cache) and of the JSON
> serialization boundary.
>
> **H2 (Concurrency Offset).** Go's goroutine concurrency model will enable Architecture B to
> achieve a concurrency ceiling comparable to or exceeding that of Architecture A's .NET
> backend, effectively offsetting the additional server-side processing inherent in SSR. The
> ceiling is defined as the sustained user-journey throughput at which the 95th-percentile
> page-journey latency first exceeds 500 ms (an enterprise SLA proxy), measured over an
> identical seeded dataset and identical journey mix on identical cluster resources.
>
> **H3 (Maintainability and Complexity).** Architecture B will demonstrate a materially
> smaller dependency surface, lower same-scanner CVE exposure, lower aggregate cyclomatic
> complexity, and shorter local development-loop times (clean build, incremental rebuild,
> edit-to-running-change) than Architecture A, reflecting the structural simplicity of a
> unified codebase and single-binary deployment model.

## Revised §3.2 — Technology Stacks

**WHAT CHANGED:** both columns rewritten to what is actually built (verified against both
repos 2026-07-18); "Ionic with Capacitor" removed (never built) and replaced by the common
Tauri v2 shell; PostgreSQL unified at 18; the shared infrastructure both systems actually run
on (Kubernetes, observability, object storage) added, since it is part of the measured
surface.

> **Architecture A (decoupled, industry default)**: React 19 as a client-side-rendered Single
> Page Application built on Next.js 15 (App Router used strictly as an application shell and
> build tool — no server-side rendering of application data), TanStack Query 5 for data
> fetching and cache management; TypeScript; a token-based design system with CSS modules;
> .NET 10 Minimal-API REST backend structured as a modular monolith (five business modules
> with per-module database schemas and migration chains, EF Core plus hand-written SQL views),
> PostgreSQL 18, RabbitMQ with a transactional outbox for integration events, WebSocket
> change notifications with fetch-then-patch client semantics, MinIO object storage with
> presigned direct uploads, JWT access/refresh authentication with Argon2id password hashing,
> and role- plus bitmask-ACL authorization resolved in SQL. Desktop packaging: Tauri v2 shell
> loading the SPA (thin client connected to the deployed backend).
>
> **Architecture B (unified monolith, challenger)**: Go 1.26 with the chi router; Templ for
> type-safe server-side HTML templating; HTMX 2.0 for partial page updates; Alpine.js for
> minimal client-side state; Tailwind CSS 4 with daisyUI, assets compiled by Bun; sqlc for
> compile-time-verified SQL and goose for migrations over PostgreSQL 18; cookie-based
> server-side sessions with PBKDF2 password hashing plus OAuth (Google/Facebook); WebSocket
> live updates delivered as server-rendered HTML fragments, fanned out across replicas via
> Redis pub/sub; MinIO object storage with server-side media ingest and on-the-fly
> thumbnailing; Tauri v2 desktop shell bundling the entire Go server as a sidecar process
> (fully offline-capable). 
>
> Both systems deploy to the same Kubernetes distribution (minikube) with
> horizontal pod autoscaling, NetworkPolicies, and an identical observability stack
> (Prometheus, Grafana, Loki, Tempo), and are load-tested with k6 under a shared scenario
> contract. The shared desktop shell (Tauri v2) deliberately holds the packaging technology
> constant so that desktop-tier measurements isolate the architectural paradigm; the
> remaining asymmetry — Architecture B's desktop build embeds its entire backend and runs
> offline, while Architecture A's is necessarily a connected thin client — is itself a
> consequence of the studied paradigms and is analyzed as a result.

_Note for the authors: if item A-1/J-7 change during implementation, this section tracks
them; do not submit before both are landed or the text is adjusted to reality._

## Revised §3.3 — Application Under Test

**WHAT CHANGED:** complete replacement. The draft described an "enterprise inventory
management and workflow dashboard" (products, orders, SSE inventory updates, reporting, audit
log) that was never built. Both systems are discussion forums with social features. The
section keeps its methodological job: defining a representative, read-heavy, moderately
complex workload with controlled relationship cardinalities.

> The benchmark application is a full-featured **online discussion forum with social
> features**, implemented independently in each architecture against a shared functional
> specification (the A/B forum specification, maintained alongside both repositories). The
> common functional core comprises: user registration and authentication; topic spaces
> (categories/communities) with public and private visibility and per-space moderation;
> threads with Markdown bodies and media attachments; nested comments to a shared maximum
> depth of five; single-action reactions (like/upvote) with per-user toggling; full-text
> search over threads; user profiles with activity statistics and karma; a friendship graph
> (request/accept/remove); one-to-one direct messaging; durable in-application notifications;
> image upload and delivery; and live updates pushed over WebSocket to all connected clients
> viewing affected content. Beyond the shared core, each implementation carries additional
> features documented in the specification as out of benchmark scope (for example, tag
> taxonomies and group conversations in Architecture A; voice messages, OAuth sign-in, and
> bilingual UI in Architecture B); all measured journeys exercise only the shared core.
>
> This domain preserves the workload characteristics the original design targeted, and
> strengthens several: the schema exercises one-to-many (space→threads, thread→comments),
> many-to-many (memberships, friendships, and — in A — thread↔tag), and self-referential
> (nested comments) cardinalities; role-based access control gates moderation on both sides;
> reads dominate writes as in typical enterprise tooling; and the real-time channel is more
> demanding than the originally planned server-sent events — every write fans out over
> WebSocket to all affected viewers, across horizontally-scaled replicas (via a message
> broker in A, Redis publish/subscribe in B). Media upload adds a controlled binary-payload
> path (client-direct presigned upload in A versus server-side ingest in B — an explicit
> paradigm difference under test).
>
> A single benchmark data tier is used, seeded deterministically and identically in both
> systems: 800 user accounts, 12 topic spaces (4 private), 1,600 threads, 9,000 comments,
> and 15,000 reactions, with content-length distributions matched between implementations.
> The tier was sized to the controlled cluster environment (Section 3.5); scaling the tier
> upward is future work.

## Revised §3.4 — Metrics

**WHAT CHANGED:** instruments replaced with the ones that exist and are verified on both
sides; the CI-pipeline timing metric replaced with local developer-loop timings; scalability
metric redefined over user journeys; VU range made realistic for the environment.

> **Resource Management.** Server tier: peak and steady-state memory (RSS) and CPU per pod,
> collected from the cluster's Prometheus (cAdvisor/kubelet series) during load runs, alongside
> replica counts under autoscaling; both systems expose application metrics natively
> (OpenTelemetry/.NET meters in A; the Prometheus Go client in B) and both serve profiling
> endpoints for targeted investigation (dotnet-counters/dotnet-trace in A, pprof in B).
> Desktop tier: process-tree RSS and CPU of the packaged Tauri applications during a scripted
> browsing session, plus installer and delivered-artifact sizes (with the structural
> difference in what each installer contains reported alongside). Delivery: production
> container image sizes and client-delivered bytes (initial page payload, raw and
> gzip-compressed).
>
> **Rendering and Network Efficiency.** Lab: Lighthouse (CLI v12) scores and Core Web Vitals
> (LCP, CLS, TBT/INP-proxy, FCP) over the two canonical pages (feed and thread detail), three
> runs per page per system, identical throttling profiles, against identically seeded
> backends. Field: both systems beacon real-user Web Vitals (web-vitals library) into
> Prometheus histograms during load runs with browser-based sessions. Payload analysis: HAR
> captures of the canonical journeys, reporting transferred bytes and request counts —
> including the structural difference between one HTML response per interaction (B) and
> JSON-API fan-out per view (A).
>
> **Developer Velocity.** Local, controlled-workstation timings, ten runs each, identical
> hardware and same-day execution for both systems: clean production build; incremental
> rebuild after a representative one-line change; full test-suite wall clock; and
> edit-to-running-change latency of each stack's development loop (both systems provide a
> scripted harness emitting machine-readable results). Cloud CI pipeline durations are
> reported descriptively (stage inventory and wall clock) but are excluded from hypothesis
> testing, as the two systems use different CI platforms whose runners are not identical
> infrastructure.
>
> **Maintainability.** Dependency counts (direct and transitive, per lockfile/module graph);
> CVE exposure from a single scanner (Trivy, pinned version, filesystem mode) run against
> both repositories on the same day; cyclomatic complexity and code-smell density from
> SonarQube (one server version, both languages, generated code excluded on both sides).
>
> **Scalability.** k6 load generation under a shared scenario contract expressing an
> identical user-journey mix (browse, read, post, comment, react, search, upload, plus held
> WebSocket connections), stepping virtual users until the SLA threshold (p95 journey latency
> 500 ms) is exceeded; the concurrency ceiling is reported in concurrent virtual users and
> sustained journeys per second, with autoscaler behaviour (replica staircase) reported
> alongside. Virtual-user ranges are sized to the controlled environment (Section 3.5) rather
> than the draft's aspirational 10,000.

## Revised §3.5 — Experimental Protocol (delta only)

**WHAT CHANGED:** environment corrected from the fictional workstation to the actual
controlled environment; `tc` network-shaping clause dropped (not validated on either side —
in-cluster ingress on the loopback path is the controlled network); repetition counts stated
honestly; leftover Polish journal-template paragraph deleted; reference [16] replaced.

> All measurements are conducted on a single controlled workstation (documented CPU/RAM in
> the artifact repository) running the same Kubernetes distribution (minikube, pinned
> version, pinned CNI) with pinned resource budgets for each system's namespace, identical
> Postgres 18 and MinIO versions, and the same observability stack. Database state is reset
> to the shared deterministic seed between measurement runs; run metadata (git SHA, image
> digest, seed counts, rate-limiter configuration, VM sizing) is archived with every run.
> Load and measurement runs are interleaved (A, B, A, B, …); each load profile is repeated at
> least N times (N stated per table; minimum 3 for exploratory profiles, higher for the
> hypothesis-tested tables) after one discarded warm-up run per system. Credential-hashing
> endpoints are excluded from hot-path latency percentiles on both sides, as the two systems
> deliberately use different password KDFs (Argon2id versus PBKDF2), and rate limiters are
> set to documented benchmark values for the duration of runs.

_Also: delete the stray template paragraph "Spis literatury powinien być numerowany…" from
§3.5, and replace citation [16] (an AWS operations book, cited for Go-vs-.NET web-server
comparisons) with an actual comparative source or drop the supported sentence._

## Revised §3.7 — Threats to Validity (additions)

**WHAT CHANGED:** three additions and one replacement; existing threats retained.

> _Internal validity (addition)._ The two implementations differ in feature breadth beyond
> the shared specification core; all quantitative comparisons are restricted to journeys
> exercising the shared core, and breadth differences are reported qualitatively. Certain
> intra-architecture design choices (identifier format: ULID strings versus 64-bit integers;
> pagination strategy: keyset versus offset; password KDF) were made independently before
> unification and are disclosed rather than normalized; their estimated impact is discussed
> with the results.
>
> _Internal validity (replacement of the tutorial-competency mitigation)._ Implementation
> bias is mitigated by cross-review of each implementation against the shared functional
> specification, and by the specification pinning observable behaviour (status codes, depth
> limits, visibility rules) rather than implementation technique.
>
> _Construct validity (addition)._ Both systems were developed with substantial assistance
> from LLM-based coding agents; developer-velocity measurements therefore characterize the
> contemporary tool-assisted development loop rather than unassisted programming effort.
>
> _External validity (addition)._ The controlled environment is a single-node Kubernetes
> cluster in a virtualized host; absolute figures are environment-specific and the study's
> claims rest on relative deltas between architectures measured under identical conditions.

## Not changed (explicitly)

- Title (registered, immutable), abstract framing, §1.1 motivation, §1.2 aims (the five
  objectives still hold — objective 5's "invert under load" phrasing matches the revised H2),
  §2 related work (HTMX/Tauri/monolith citations remain apt; only [16] needs repair),
  §3.1 research design and GQM table, §3.6 statistical analysis (Shapiro-Wilk /
  Mann-Whitney U / Cliff's delta — all still appropriate for the interleaved repeated runs).
