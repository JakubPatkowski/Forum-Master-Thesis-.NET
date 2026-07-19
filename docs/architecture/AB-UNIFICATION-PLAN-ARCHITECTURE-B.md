# Unification Proposal — Architecture B (gomx)

_Written 2026-07-18 by the A side. **STATUS: PROPOSAL — for Hubert to review, amend, or
reject.** Nothing here has been applied to gomx (the repo was examined strictly read-only);
every item is phrased as "we propose", and each carries the evidence that motivated it.
Companion: `AB-UNIFICATION-MASTER-PLAN.md` (joint items J-1…J-7 need us both),
`docs/specs/forum-spec.md` (the draft contract this feeds)._

First, credit where due — things B has that the comparison leans on and A is adopting or
mirroring, not the other way around: the Tauri shell as the common desktop vehicle, the
Lighthouse/RUM Web Vitals instrumentation pattern (`internal/metrics/metrics.go:137-147`),
the `reload-bench` dev-loop harness (`scripts/reload-bench.sh`), trivy/SonarQube as the
common scanners, and the size-metrics scripts. The proposals below are the smaller reverse
direction: what the benchmark needs from B.

---

## B-1 — Deterministic benchmark-scale seed  (BLOCK, ~1–2 days)

**Why:** H2/Q5 (concurrency ceiling) is invalid across mismatched datasets. Today
`shared.Seed(store, blobs, 24)` runs on an empty store/DB
(`internal/server/forum.go:78-84`) — ~24 posts and a handful of communities. A's benchmark
seed is 800 users / 12 spaces / 1600 threads / 9000 comments / 15000 reactions (measured
24 MB in Postgres; volumes proposed as the joint baseline in J-2, negotiable).

**Proposal:**
- A `seed-bench` mode (env or subcommand) writing the agreed volumes through the Postgres
  store; keep the current demo seed as-is for dev/desktop.
- Deterministic content (fixed RNG seed) so re-runs produce identical row counts and
  comparable text lengths; A can share its word-bank + Zipf distribution parameters
  (`Forum.Infrastructure/Seeding/`, `load/k6/lib/assets.js`) so body-size distributions match.
- One precomputed PBKDF2 hash reused across seeded users (A does exactly this with Argon2id)
  so seeding stays fast; bench users need a known password for k6 logins.
- An idempotency guard (refuse non-empty DB without `--force`), mirroring A's, so a stale
  dataset can't silently pollute a run.

**Acceptance:** row counts queryable and equal to spec §7; two consecutive seeds of a fresh DB
produce identical counts; documented in the run protocol.

## B-2 — k6 profiles matching the joint scenario contract  (BLOCK, ~1 day after J-4)

**Why:** `loadtest/forum.ts` drives a readers+writers mix with its own weights; A drives a
9-action journey mix with staged profiles (smoke/demo/stress) and per-endpoint thresholds.
For H2 the two scripts must express the same user behaviour and report the same unit
(journeys/sec), even though the wire traffic differs by paradigm (HTML vs JSON) — that
difference is the finding, the behaviour must not be.

**Proposal:** extend `forum.ts` (or add `forum-bench.ts`) with: the agreed journey mix and
think times (spec §9), three staged profiles with the same VU ramps as A's, thresholds bound
to the same SLA (p95<500 ms pages, error rate, zero-429 guard), journey-tagged metrics, and
the `===SUMMARY_JSON===` stdout block (or equivalent) for archival. Keep the existing
remote-write→Grafana flow — it's better live tooling than A has and nothing about it hurts
comparability.

## B-3 — CI Postgres 16 → 18  (BLOCK, minutes)

**Why:** internal inconsistency found during the audit: deploy pins `postgres:18-alpine`
(`deploy/k8s/postgres.yaml`) but the adapter integration job tests against `postgres:16-alpine`
(`.gitlab-ci.yml:201`). With J-7 unifying both architectures on 18, the CI service should be
18 too so the tested engine is the deployed engine.

## B-4 — Benchmark-mode limiter settings  (BLOCK, hours)

**Why:** the auth limiter defaults to 10 requests/min/IP (`web/server.go:245-247`), and k6
setup traffic comes from one IP — logging in the seeded bench-user pool would trip it
instantly. A hit exactly this and had to raise its limiter for runs and restore after
(`scripts/bench-run.sh`).

**Proposal:** a documented benchmark override (`GOMX_AUTH_RATELIMIT` already exists — the
proposal is just to *pin the values used during runs in the shared protocol and record them
in the run metadata*, so neither side quietly benchmarks with a different throttle). Same for
`GOMX_BEACON_RATELIMIT` if RUM stays enabled during runs.

## B-5 — Feed pagination: measure offset at scale, adopt keyset only if it shows  (NICE)

**Why (and why it's NICE not BLOCK):** `ListPostsPage`/`ListVisiblePostsPage` are
limit/offset (`shared/models.go:323,358`); A is keyset-only. At the proposed 1600-thread
volume, offset depth is bounded and Postgres will likely not care; at the PDF's original 100k
tier it would. We propose **not** pre-emptively rewriting: run the joint stress profile at J-2
volumes first; if deep-page journeys show offset degradation, adopt keyset for the feed
queries then. Either way the paper documents the strategies as an intra-architecture design
difference (threat-to-validity §3.7 addition).

## B-6 — Run-archival metadata  (REC, ~½ day)

**Why:** reproducibility of the thesis tables. A archives per-run bundles
(`thesis/results/A/<stamp>-<profile>/`: `meta.json` with git SHA + image digest + seed counts
+ limiter values + VM sizing, k6 summaries, sampler JSON, Prometheus query snapshots,
`stats.json` mean±stddev).

**Proposal:** a small runner script producing the same `meta.json` fields + k6 summary per
run into `thesis/results/B/…` (layout in spec §9). The existing `testid` convention slots in
directly as the run stamp. We can share A's `stats.json` aggregation snippet.

## B-7 — Dev-loop measurements with the shared protocol  (BLOCK, hours)

**Why:** J-5/J-6 replace the PDF's unrunnable "CI on identical infrastructure" metric with
local developer-loop timings. B already has the best harness in either repo
(`task reload-bench`); the ask is only alignment: same measured steps (clean build,
incremental build, test wall-clock, edit→running-change, cold start), same N (10), same
machine/day as A's runs, same JSON schema (we propose B's existing
`.bench/reload-bench.json` shape as the standard and A adapts to it).

## B-8 — Same-day scanner runs  (BLOCK, coordination only)

**Why:** H3's CVE/complexity numbers must come from the same tool versions on the same day.
B already has both tools configured (trivy in CI, `sonar-project.properties`). The ask:
one coordinated scan day — pinned trivy version + shared SonarQube instance/version — and the
raw reports exchanged. Nothing to build.

---

## Questions for Hubert (needs-verification list from the read-only audit)

1. **Sessions under load:** every authenticated request resolves the cookie via
   `UserForToken` — is that one Postgres round-trip per request, or is there an in-process
   cache? (`postgres.go` was not fully audited.) It affects how we phrase the
   stateless-vs-stateful comparison, and whether B wants a session cache before the stress
   profile.
2. **`sessions` table TTL enforcement:** `SessionTTL` is 30 d (`models.go:637`) — is expiry
   enforced in the query and rows cleaned up, or only advisory? (Benchmark seeding will mint
   ~800 sessions.)
3. **WS fragment payloads under fan-out:** rendered-HTML broadcasts are per-event, per-pod —
   at the stress profile's WS population, is the Redis payload volume something you want
   measured/capped? (A measures pushes-sent; the joint WS scenario will need an agreed
   receipt-latency measurement point on both sides.)
4. **Community count at bench scale:** spec §7 proposes 12 spaces incl. 4 private — does B's
   private-membership model (pending approvals) need seeded members the way A seeds ACL
   grants?
5. **Mobile claim symmetry:** MOBILE.md is decided-not-built; A will mirror with a doc-only
   plan. OK to present both papers' mobile claims as "documented path, not shipped"?
6. **CI runners:** are your GitLab jobs on gitlab.com shared runners or self-hosted? (Only
   affects how we caveat the descriptive CI wall-clock table — J-6.)

## Explicitly NOT proposed to B

ACL/RBAC engine, audit columns/soft-delete, ULID keys, outbox+broker messaging, group chat,
peer blocks, tags — see the master plan's "What deliberately does NOT get leveled" section.
These stay qualitative comparison material; forcing them into B would destroy the
architectural authenticity the thesis needs (a unified monolith is *supposed* to be simpler).
