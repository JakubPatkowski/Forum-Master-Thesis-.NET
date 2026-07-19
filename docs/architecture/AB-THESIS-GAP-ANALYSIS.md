# Thesis Draft ↔ Reality Gap Analysis

_Written 2026-07-18. Reconciles the ~6-month-old draft manuscript (the attached PDF,
"Artykuł_naukowy_czI…") against what `forum-dotnet` (A) and `gomx` (B) actually are today.
Evidence base: `AB-CURRENT-STATE-AUDIT.md`. The `thesis/` directory contains only the archived
9c benchmark run (`thesis/results/A/20260716-1643-demo/`) — **no newer manuscript material
exists in the repo**, so the PDF is the only manuscript source and everything below reconciles
against it._

Classification per gap: **(a) update the paper**, **(b) change the code**, **(c) middle path**.

The registered title is immutable and is not restated or reworded here.

---

## T. The title question: is "Cross-Platform Applications" honored today?

**Short answer: by B, yes; by A, no.** B ships a working Tauri v2 desktop shell with the Go
server as a bundled sidecar (`gomx/src-tauri/tauri.conf.json`, CI job `build:sidecar`) and a
decided, partially-scaffolded mobile plan (`gomx/MOBILE.md`: hosted-shell option chosen,
per-platform Tauri configs committed). A has **no packaging of any kind** — the PDF's §3.2
claim "Ionic with Capacitor for desktop wrapping" describes something that was never built
(zero matches for capacitor/ionic/tauri/electron in `frontend/package.json`).

**Minimum viable fix (classification b, on A):** add a **Tauri v2 desktop shell to A** whose
webview loads the SPA. Rationale for Tauri over the PDF's Ionic/Capacitor or Electron:

1. **Controlled experiment.** With the same shell technology on both sides, the desktop shell
   is held constant and the only variable inside the window is the architecture — exactly what
   the methodology (Wohlin, §3.1) wants. Electron on A would confound "decoupled architecture"
   with "heavier shell" and hand B a trivial, uninformative win on every resource metric.
2. **H1 stays testable.** H1 attributes B's expected footprint advantage to "the elimination
   of a heavy JavaScript runtime". Under a common Tauri shell that reduces to what actually
   differs: the SPA bundle + client runtime (React/React Query/hydrated state) vs.
   server-rendered HTML + ~small HTMX/Alpine bundles. That's the honest version of H1.
3. **Effort.** Days, not weeks — B's own MOBILE.md estimates the equivalent hosted-shell work
   at "days"; A's is the same shape (shell + URL/static assets), with no sidecar complexity.

Two decisions inside that fix (detailed in `AB-UNIFICATION-PLAN-ARCHITECTURE-A.md` item A-1):
whether the shell embeds the exported SPA bundle or loads the hosted URL, and how the API base
URL is configured. Either way, **A's desktop app is a connected thin client while B's is a
full offline stack** — a real, paradigm-driven asymmetry the paper should present as a
finding (a unified monolith compiles into an offline desktop app; a decoupled tier cannot
bundle its API + database into an installer).

**Mobile:** neither side ships it; B has a committed plan + configs. Honest scope for the
paper: cross-platform = web + desktop (measured), mobile = documented feasibility path on both
sides (A should write its equivalent of MOBILE.md — a PWA/hosted-Tauri note — so the two
papers' claims are symmetric). Classification: (c).

**On the underlying doubt ("is desktop really required for A?"):** given the immutable title
and the fact that B's shell exists, is in CI, and anchors §2.5 of the related-work section —
yes. Skipping it would leave the title unsupported for exactly one of the two systems under
test, which a reviewer will find in minutes. It is also the cheapest of all the blocking
items. If it is skipped anyway, the fallback is a paper-side reframe ("cross-platform =
browser-delivered ubiquity for A"), which is defensible prose but visibly weaker than the
five-day fix; not recommended.

---

## §1.3 Hypotheses H1–H3 — still meaningful?

### H1 (resource efficiency) — **keep, reword slightly** (a)
Still well-posed for both tiers of measurement:
- **Server side**: peak RSS / CPU per pod under the agreed load — both stacks are
  instrumented (A: Prometheus + `kubectl top` sampler in `scripts/run-load-test.sh`; B:
  kube-prometheus-stack, `deploy/monitoring/`). A's archived demo run already demonstrates the
  measurement path works.
- **Desktop side**: measurable only after A-1 (shell). Under a common Tauri shell, reword the
  mechanism clause: not "elimination of a heavy JavaScript runtime" (both use the OS webview)
  but "elimination of the client-side application runtime (bundle, hydration, client cache)".
- **Binary/bundle size**: B already publishes these as CI metrics
  (`scripts/ci-artifact-metrics.sh`); A needs the equivalent measurement (one script; A-8).

Caveat to add to the paper: A cannot ship a self-contained desktop binary at all, so the
"binary size" comparison needs a defined basis — installer size + a documented note that A's
installer excludes the server tier while B's includes it. That is a result, not a nuisance.

### H2 (concurrency offset / SSR penalty) — **keep; sharpen the definition** (c)
Testable and interesting, but two conditions must hold first:
1. **Same dataset scale.** B currently benchmarks over a ~24-post demo seed
   (`gomx/app/forum/shared/seed.go`); A over 800 users/1600 threads/9000 comments. Comparing
   concurrency ceilings across those datasets would be meaningless. B needs the agreed
   benchmark seed (B-1).
2. **Same load semantics.** "Requests" differ by paradigm (A: JSON API calls, a page = 2–4
   calls; B: one HTML response per page + fragment swaps). The ceiling must therefore be
   defined over **user journeys per second** (page-opens, posts, votes), not raw RPS — the
   revised §3.4 in `THESIS-REVISED-SECTIONS-DRAFT.md` does this. The p95<500 ms SLA threshold
   from the PDF can stay as the ceiling criterion; both k6 suites already use p95 thresholds
   (A `load/k6/main.js`; B `loadtest/forum.ts`).
Also note the PDF's "10 to 10,000 virtual users" is fantasy for a 10 GiB minikube VM; A's
measured knee territory is ~150 VU on the current sizing. Revise the range to what the shared
environment supports (e.g. 10→500) and step until the SLA breaks.

### H3 (maintainability & complexity) — **keep dependency/CVE/complexity; replace the CI clause** (c)
- Dependency surface: countable today (A: CPM `Directory.Packages.props` + `package.json`
  lockfiles; B: `go.mod` 20 direct + `package.json` 10 direct). Define "direct vs. transitive"
  counting rules in the protocol.
- CVE exposure: only comparable if the **same scanner** runs on both repos the same day
  (trivy fs — B already runs it in CI, A adds it; A-6). NVD-based per-ecosystem counts from
  different tools are not comparable.
- Cyclomatic complexity: SonarQube supports both Go and C#; B has `sonar-project.properties`,
  A needs a scanner config (A-7). Exclude generated code on both sides (B already excludes
  `*_templ.go`, `postgres/db/`; A must exclude EF migrations — precedent: those folders are
  already marked `generated_code=true` in `.editorconfig`).
- **"CI/CD pipeline execution time across ten runs on identical controlled infrastructure" —
  not achievable as written** and should be dropped (a). A runs 2 GitHub Actions jobs on
  GitHub-hosted runners; B a 7-stage tiered GitLab pipeline on GitLab runners. Neither the
  hardware nor the job scopes match, and normalizing them would mean rebuilding one side's CI
  on the other's platform — busywork with no architectural signal. **Replacement metric
  (b, small):** local, workstation-controlled developer-loop timings, 10+ runs each:
  clean build, incremental rebuild, test-suite wall clock, and edit→running-change latency.
  B has a purpose-built harness for the last one (`task reload-bench` →
  `.bench/reload-bench.json`); A mirrors it (A-9). Raw CI wall-clock can still be *reported*
  as descriptive data with an explicit non-comparability note.

## §3.2 Technology stacks — rewrite both columns (a)

Wrong today (verified):

| PDF claim | Reality |
|---|---|
| A: "TanStack Router… Vite 6" | Next.js 15 App Router as CSR shell + TanStack (React) Query 5 (`frontend/package.json`) |
| A: "Shadcn/ui… Tailwind" | Hand-rolled CSS modules + design tokens (`frontend/src/styles/tokens/`) |
| A: "Ionic with Capacitor for desktop wrapping" | Nothing — see §T |
| A/B: "PostgreSQL 18" | A pins 17 (`compose.yaml:4`, `k8s/postgres/statefulset.yaml:28`); B deploys 18-alpine but tests on 16-alpine in CI (`.gitlab-ci.yml:201`) |
| B: "Bun with Tailwind CSS" | Correct, plus daisyUI, goose, sqlc, chi, coder/websocket, Redis, MinIO, go-i18n, OTel — the PDF's B column is roughly right but incomplete |
| B: "Alpine.js for minimal client-side state" | Correct; add HTMX 2.0 (PDF says 2.0 — fine) |

Missing from both columns: the infra that now dominates both systems (Kubernetes, Prometheus/
Grafana/Loki/Tempo, RabbitMQ vs. Redis, MinIO). §3.2 should present the stacks as they are —
the full rewrite is in `THESIS-REVISED-SECTIONS-DRAFT.md`. Decision embedded there: **unify
PostgreSQL on 18** (b on A: bump two manifests; proposal to Hubert: align CI service to 18).

## §3.3 Application under test — full rewrite (a)

The described "enterprise inventory management and workflow dashboard" (products, orders,
state machines, SSE inventory updates, reporting, audit log) matches neither repo and never
will. Both systems are **forums with social features**. The rewrite (drafted in
`THESIS-REVISED-SECTIONS-DRAFT.md`) keeps the section's *job* — defining a representative,
read-heavy, moderately complex CRUD workload with relationship-cardinality coverage — and
maps it onto the real domain:

- one-to-many: thread→comments, community→posts; many-to-many: A `thread_tags`, B
  memberships/friendships; self-referential: nested comments (depth ≤5 on both);
  real-time: WebSocket live updates (richer than the PDF's SSE claim);
  RBAC: present on both (A much deeper), full CRUD: present.
- The data-scale tiers (1k/10k/100k) are replaced by the agreed seed volumes
  (`docs/specs/forum-spec.md` §7) — one shared tier now, with headroom documented rather than
  three speculative tiers nobody has run.

Notably the forum domain is *stronger* than the invented dashboard for this thesis: it adds
fan-out real-time, media upload paths, and social-graph queries, all of which differentiate
the paradigms. The rewrite says so explicitly ("why it's still representative").

## §3.4 Metrics — reconcile plan vs. instrumentation (c)

| PDF instrument | Status A | Status B | Action |
|---|---|---|---|
| pprof (B) / dotnet-counters (A) | OTel metrics + Prometheus exist; dotnet-counters usable ad hoc | pprof opt-in (`GOMX_PPROF`, `router.go:119`) | Keep; name what's actually used |
| Prometheus + node_exporter | Full stack (10c) | kube-prometheus-stack (`deploy/monitoring/`) | Keep |
| Lighthouse CLI v12 + web-vitals lib | **Absent** | Lighthouse CI + RUM beacons live | **A-2/A-3 (blocking for Q2)** |
| HAR payload capture | Manual, undocumented | Manual, undocumented | Add one protocol step (joint) |
| CI/CD ×10 identical infra | Not comparable | Not comparable | Replace (see H3 above) |
| Local build times | Ad hoc | `task reload-bench` harness | A mirrors (A-9) |
| Dependency counts + NVD CVEs | NuGetAudit weekly | govulncheck + trivy in CI | Same-day trivy on both (A-6) |
| SonarQube complexity | **Absent** | Configured | A-7 |
| k6 10→10,000 VU | 3 profiles, archived runs | 1 script, live Grafana | Joint scenario + realistic VU range |

## §3.5 Experimental protocol — update to the real environment (a, plus one honesty item)

- The stated workstation (Ryzen 9 7900X, 64 GB, Ubuntu 24.04 bare metal) does not exist in
  this project; runs happen on a WSL2 host with a 12 GB VM and a 10 GiB minikube profile
  (A `.env`/runbook; B `task k8s:up` on the same class of machine). Either the paper's
  environment section is rewritten to the actual shared environment (recommended — it is
  controlled and documented), or the final runs are executed on such a workstation if one is
  actually available. Do not leave the fictional hardware in.
- `tc` network shaping: never used by either harness. Either add it to the joint protocol
  (both sides, one shaping profile) or drop the claim. Recommendation: drop for the in-cluster
  runs (ingress on loopback is the controlled path; shaping adds a variable neither side has
  validated) and note LAN-emulation as future work.
- 30 repetitions minimum: A's harness supports `--repeats N` (`bench-run.sh`); the archived
  run used 3. The final protocol should state the number actually used per profile and meet
  it on both sides. Interleaving A,B,A,B and the 60 s warm-up are both implementable — A's
  harness already does a discarded warm-up; keep.
- **Leftover template text:** §3.5 still contains the Polish journal-template paragraph
  ("Spis literatury powinien być numerowany…") — delete it before any submission.
- Reference [16] (Wittig & Wittig, *Amazon Web Services in Action*) is cited as evidence for
  "Go vs .NET web server comparisons" — it is not that; find a real citation or drop the
  sentence.

## §3.7 Threats to validity — additions required (a)

Keep the three existing threats; add:
1. **Feature-breadth asymmetry** — each system has features the other lacks (audit §1);
   mitigated by pinning benchmark journeys to the shared core (forum-spec §5) and reporting
   only those.
2. **KDF asymmetry** — Argon2id (A) vs. PBKDF2-100k (B); login endpoints excluded from
   hot-path percentiles on both sides.
3. **ID/pagination strategy asymmetry** — ULID strings + keyset (A) vs. bigint + offset (B);
   an intra-architecture design choice, disclosed rather than normalized.
4. **Environment** — WSL2/minikube single-node; absolute numbers are environment-specific,
   deltas are the claim.
5. **AI-assisted development** — both codebases were substantially built with LLM agents;
   developer-velocity metrics measure the human+agent loop, not solo typing. (This is honest,
   increasingly normal, and better disclosed than discovered.)
6. **Same-authors bias already listed** — keep, but the mitigation "tutorial completion"
   reads as filler now; replace with the real mitigation: cross-review of each other's
   implementation against the shared spec (`docs/specs/forum-spec.md`).

## What would bias the benchmark if left unfixed (priority list)

1. **Dataset scale mismatch** (B's 24-row seed) — invalidates H2/Q5 outright.
2. **Postgres 17 vs 18** — cheap to fix, indefensible to leave.
3. **No CWV instrumentation on A** — Q2 has no data for one side.
4. **No desktop shell on A** — the title and H1's desktop half have no A-side object.
5. **Unmatched load scenarios** — A's mix (9 endpoint classes) vs B's (readers+writers)
   measure different behaviour; joint journey mix required.
6. **Rate limiting**: A raises limits for benchmarks (`bench-run.sh`); B has a 10/min auth
   limiter (`web/server.go:245`) that a 200-login setup phase would trip. Joint protocol must
   pin limiter settings on both sides.
7. **Same-scanner CVE/complexity** — otherwise H3 numbers are apples/oranges.
