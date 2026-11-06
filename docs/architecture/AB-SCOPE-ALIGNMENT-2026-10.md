# A/B alignment — decision record (2026-10-16, final same-day revision)

**STATUS:** DECIDED on the A side (Jakub); B-side changes delegated to Hubert for 2026-10-16/17 via
`AB-ALIGNMENT-INSTRUCTIONS-B.md`. Supersedes the two earlier same-day drafts of this file (the first
proposed cutting 5–9k LOC from B; the second was a 7-item fix list without code-level verification).

## The two documents this record points to

| Document | For whom | Contents |
|---|---|---|
| [`AB-ALIGNMENT-INSTRUCTIONS-A.md`](AB-ALIGNMENT-INSTRUCTIONS-A.md) | Jakub + Claude sessions | §1 verification table of every plan item (CONFIRMED/WRONG/UNCERTAIN with `path:line`), §2 golden-mean decisions, §3 work items A-01…A-18 with code, §4 interface contract, **§5 Day-0 runbook incl. fallback**, §6 deviations register, §7 open decisions |
| [`AB-ALIGNMENT-INSTRUCTIONS-B.md`](AB-ALIGNMENT-INSTRUCTIONS-B.md) | Hubert (self-contained; assumes no access to A's repo) | §1 fairness rules, §2 same decision table, §3 work items B-01…B-15 with Go/YAML before/after, §4 **identical** contract, §5 definition of done for tomorrow + self-check, §6 identical deviations, §7 identical open decisions, §8 what is NOT asked |

Snapshots: A = `forum-dotnet` `29-feat-phase-12---tauri-desktop-application` @ `ee0faee` + staged;
B = `gomx` `master` @ `8b23bc1`. Shared-contract draft `docs/specs/forum-spec.md` still awaits
Hubert's sign-off; the instruction documents are the executable subset of it for the first classes.

## What changed versus the earlier plan (and why)

| Area | Earlier plan | Decided | Reason (evidence in the A document §1) |
|---|---|---|---|
| Harness | B journeys possibly in B's repo | **one harness in A's repo** (`load/k6/main.js` + `targets/{a,b}.js`); Hubert's `forum.ts` stays for his dashboards only | `forum.ts` has three defects (shared session, `?page=2` ignored by the handler — cursor-only paging, 201 vs 202) and drift between two scripts is exactly what fairness forbids |
| HPA on Day 0 | HPA 1–3 @ 70 % on both | **fixed 2 replicas, no HPA, both**; HPA class later with A's `hpa.yaml` copied to B | removes autoscaler policy differences, CPU-init period and Completed-pod pollution from the first numbers |
| Resources | "common" unspecified | app **250m/256Mi → 1000m/512Mi**; Postgres **A's args** (`shared_buffers=256MB`, `effective_cache_size=512MB`, `max_connections=100`) + 250m/512Mi → 1000m/1Gi; pool 30 on both via `GOMX_DB_MAX_CONNS` (a `pool_max_conns` URL param crashes B's goose step — found live) | B had defaults + pgxpool `max(4, NumCPU)` = 8 (verified from pgx v5.11.0 source) |
| TLS | open | **B behind TLS** (`gomx.local` SAN in A's mkcert cert, `gomx-tls` secret, `GOMX_SECURE_COOKIES=1`) | ≈20 min on A, one overlay file on B; equalizes transport + cookie posture |
| Compression | "env flag to disable B's gzip" | confirmed; **`GOMX_COMPRESS=0`**; A's ingress gzip ConfigMap is cluster-global so B is compressed by it | `router.go:56` unconditional; nginx won't double-compress |
| Tracing | off both | confirmed, but A's "empty `Otlp__Endpoint`" is **not off** (exporter always registered → silent failures); use `OTEL_TRACES_SAMPLER=always_off` (verify) or a config guard | `ObservabilityExtensions.cs:38-44` |
| "Writes rejected" limiter in B | suspected throttle | **not a throttle** — domain-rejection funnel; but it revealed that **posting needs community membership** → harness picks a member community per user | `rules.go:229-232` |
| B seeding | "1 replica first boot" | confirmed **plus** a second defect: the HTTP listener starts after seeding, so the base liveness probe kills a seeding pod → partial dataset → overlay adds a `startupProbe`; P1 advisory lock around IsEmpty→seed | `cmd/server/main.go:44,60`, `forum.go:112-114` |
| Z1 tests | "update B tests asserting 8/10" | **no such tests exist**; nothing to update | grep over `*_test.go` and Playwright specs |
| D2 | "B 2 private" | 2 private but **~200 members each** (not 25); P1 alignment | `seedbench.go:155-170` |
| D4 | "A Zipf" | A is squared-uniform, not Zipf; B 25 %-on-1 % | `SeedDistribution.cs:17-18` |
| D5 | 12000/3000 | plus **10 % downvotes** in B's seed → B drops them in bench (P1) | `seedbench.go:270-284` |
| D6 | word bank vs 10 sentences | confirmed, plus: with a 31-word bank every word hits nearly every body → **J6 is a match-everything query on both sides** → new open decision (shared selective bank) | `SeedText.cs:12-18`, `ContentSeeder.cs:120` |
| Day-0 mix | A's 9c mix incl. J4/upload/tags | **`v0-day0`: J1 33 / J2 22 / J6 11 / nav(list spaces) 8 / J3 11 / J5 13 / J7 2**; J4+image/J8/J9 P1 | every measured journey must exist on both sides |
| J1 on A | feed page only | **feed page + reaction batch** (the SPA's real render) | parity with what the browser does |
| B CI Postgres 16→18 (old B-3) | asked | **already done** (`.gitlab-ci.yml:197`) | stale item |
| Monitoring | "both stacks fit in 10 GiB?" | **only A's stack**; B's ServiceMonitor applied to it (works: Prometheus selects all namespaces) | two kube-prometheus-stacks in one `monitoring` ns would collide |
| New finding | — | **A's Content read endpoints do no private-category check** (authz gap + fairness asymmetry) → A-12, P1 | `grep Visibility.Private` in Content Application |
| Physical cuts | none | **none** — confirmed after verification | no measured-path impact a flag cannot neutralize |

## Implementation status (A side, 2026-10-17)
A-01 harness adapters, A-02 runner, A-03 bench orchestrator, A-04 TLS SAN, A-05 resources, A-07 tracing toggle, A-08 deploy script + A-hosted overlay `k8s/gomx-bench/` — done and locally verified (both-target k6 smoke 0 % failed; Api tests 53/53). Cluster steps (runbook §5) executed the same night against unmodified gomx master: `thesis/results/DAY0-2026-10-17.md` (A 73.6 vs B 76.4 journeys/s at 80 VU; app pods mem 433 vs 79 MiB; B page size 8/10 deviation recorded).

## Implementation status (2026-10-17 evening — fairness review + v1 harness)
Hubert's `feature/master-thesis` @ `e13f18f` reviewed (B-05a missing → startup crash; fixed on top, see the B
document's status box). A-12 (private-category read gate) implemented. Harness v1 (mix `v1`, harness
`h2-2026-10-17`): whole-corpus thread pools, shared search words, A J5 real toggle, gzip on every request (k6 sends
no `Accept-Encoding` by default — Day-0 compressed nothing), scenario-only windows, `warmup` + open-model `capacity`
profiles. New tooling: `scripts/ab-round.sh` (unattended interleaved round, reseed + fresh pods per run, profile
verification), `ab_window.py`, `ab_report.py`. Both sides now run identical Postgres 18.4 (Debian). Day-0 erratum in
`thesis/results/DAY0-2026-10-17.md` §0; v1 methodology + results in `thesis/results/AB-COMPARISON-v1.md`.

## Implementation status (2026-10-18 night — independent re-verification)
A second session re-executed the whole checklist (`AB-START-HERE.md` §5.0) and counted the SQL behind every journey on
local copies of both seeds. Fixed: B's anonymous `GetMembership` lookups (12 per `nav`, 1 per J1 — Go edit + test, for
Hubert's review); A's missing owner indexes (profile reads were seq scans — Content migration); B's reseed now leaves
fresh pods like A; every run records an idle window → standing vs marginal CPU per journey in the report. Recorded as
deviations D9–D11 (A's per-endpoint gate + Npgsql reset, A's live ACL resolution per write, B's full-body cards with
numbers). Both images rebuilt from the committed trees before the first measured round.

## Decisions taken 2026-10-17 (Jakub, after the fairness review)
Implement every P0–P2 fix of the review; keep and document the remaining differences as **D1–D7** (recorded in
`thesis/results/AB-COMPARISON-v1.md` §6.2): D1 idealized SPA client (A cost = lower bound; real-waterfall replay in
v2), D2 B's feed/search previews stay (product difference), D3 anonymous reads in v1 (logged-in variant in v2), D4
non-selective search kept and disclosed (shared selective bank before hypothesis-tested runs), D5 seed timestamps
recorded (neutralized for thread journeys), D6 social/tags/soft-delete seed differences recorded, D7 ≥ 5 interleaved
repeats with a reseed before every run. Added during the implementation: D8 runtime memory policy (A Server GC, B Go
defaults) stated with every memory number. Entry point for everything A/B: `AB-START-HERE.md`.

## P0 effort (from the documents)
- **A:** harness adapters 3–5 h · runner/bench scripts 2–3 h · TLS/resources/tracing/ServiceMonitor/deploy script ≈ 1.5 h · Day-0 execution ≈ 5–6 h wall clock (builds + 2 × (smoke + 3 demo) + static metrics).
- **B:** B-01 15 min · B-02 45 min · B-03 30 min · B-04 20 min · B-05 overlay 1–1.5 h · tests/lint ≈ 30 min → **≈ 3.5–4 h**.
- **Fallback (A doc §5.9):** B in plain docker with the same limits (indicative numbers) → static metrics + Lighthouse + A demo only → reads-only mix `v0-reads` if the adapter is late.

## Still open (one line each; details in §7 of both documents)
HPA vs fixed (rec. fixed) · TLS for B (rec. yes) · tracing off both (rec. yes) · Day-0 mix without J4
(rec. yes) · who moves on D2–D7 (rec. B adopts A's generators; A adopts B's social volumes) · shared
selective word bank before stress runs (rec. yes, both sides).
