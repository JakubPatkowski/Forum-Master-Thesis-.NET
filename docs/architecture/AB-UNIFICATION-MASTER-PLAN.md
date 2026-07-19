# A/B Unification Master Plan

_Written 2026-07-18. The single prioritized plan tying together `AB-CURRENT-STATE-AUDIT.md`
and `AB-THESIS-GAP-ANALYSIS.md`. Item IDs: **A-n** = forum-dotnet only (detail in
`AB-UNIFICATION-PLAN-ARCHITECTURE-A.md`), **B-n** = gomx only (**proposals for Hubert**,
detail in `AB-UNIFICATION-PLAN-ARCHITECTURE-B.md`), **J-n** = joint agreement, **P-n** =
thesis document. Priority: **BLOCK** = blocking for a scientifically valid comparison,
**REC** = strongly recommended, **NICE** = nice-to-have._

_GQM mapping keys: Q1 resources, Q2 rendering/network, Q3 dev velocity, Q4 maintainability,
Q5 concurrency ceiling; H1/H2/H3 as in the draft._

---

## Phase U0 — Joint agreements first (everything else hangs off these)

| ID | Item | Why | Prio | Effort | Serves |
|---|---|---|---|---|---|
| J-1 | **Sign off `docs/specs/forum-spec.md`** (drafted, `STATUS: DRAFT`) — shared functional core, benchmark journeys, out-of-scope lists | Without a pinned scope every measurement argument is re-litigable | BLOCK | 1 review session with Hubert | all |
| J-2 | **Benchmark dataset volumes** — proposal: adopt A's Benchmark seed shape (800 users / 12 spaces / 1600 threads / 9000 comments / 15000 reactions; ~24 MB measured on A) | H2/Q5 invalid across mismatched datasets; volumes already proven to fit the 10 GiB VM | BLOCK | decision only | Q1 Q5 H2 |
| J-3 | **Environment & resource budget** — same workstation class, minikube CPU/RAM, replica floor/ceiling, HPA targets, Postgres `max_connections`, rate-limiter settings pinned per side | "identical controlled infrastructure" must be true where it can be (the cluster), documented where it can't | BLOCK | ½ day writing into forum-spec §8 | Q1 Q5 |
| J-4 | **Load-scenario contract** — journey mix (feed page, thread open, comment, post, react/vote, search, upload, WS hold), think times, VU ramps, thresholds; journeys/sec as the unit, not raw RPS | A and B currently drive different behaviour; RPS is paradigm-skewed (1 page = N JSON calls on A vs 1 HTML on B) | BLOCK | 1 day each side to align k6 scripts after agreement | Q2 Q5 H2 |
| J-5 | **Measurement-tool parity** — trivy fs (CVEs) + SonarQube (complexity) run on both repos same-day; dependency-count rules (direct vs transitive); local build-time protocol (10 runs, same machine) replacing the PDF's CI-timing clause | H3 otherwise compares tool outputs, not systems | BLOCK | ½ day | Q3 Q4 H3 |
| J-6 | **CI comparability stance** — agree to *report* both pipelines descriptively (stages, scope, wall clock) and *benchmark* only local developer-loop timings | GitHub-hosted vs GitLab runners are not identical infra and never will be | BLOCK | decision only | Q3 H3 |
| J-7 | **Postgres 18 everywhere** — A bumps 17→18 (2 manifests); B aligns CI service 16→18 | Same DB engine major version is table stakes for §3.2's isolation claim | BLOCK | hours | all |

## Phase U1 — Architecture A obligations (summary; full backlog in the A plan)

| ID | Item | Prio | Effort | Serves |
|---|---|---|---|---|
| A-1 | **Tauri v2 desktop shell** for the SPA (same shell tech as B ⇒ shell held constant) + an `A-MOBILE.md` feasibility note mirroring B's `MOBILE.md` | BLOCK (title, H1-desktop) | ~3–5 days | H1 Q1 |
| A-2 | **Lighthouse harness** (CLI v12, same pages/run-count philosophy as B's `lighthouserc.json`) | BLOCK (Q2) | 1 day | Q2 H2 |
| A-3 | RUM Web Vitals beacon (web-vitals lib → a `/vitals`-style endpoint → Prometheus histograms, mirroring B's `internal/metrics` design) | REC | 1–2 days | Q2 |
| A-4 | Postgres 17→18 (`compose.yaml`, `k8s/postgres/statefulset.yaml`) + verify migrations/FTS/ACL suites green | BLOCK | ½ day | all |
| A-5 | Frontend in CI (typecheck/lint/vitest/build) + container image build job | REC (Q3 honesty) | ½ day | Q3 H3 |
| A-6 | trivy fs scan (local script + optional CI job) | BLOCK (H3 CVE metric) | ½ day | Q4 H3 |
| A-7 | SonarQube scanner config for C#+TS with generated-code exclusions | BLOCK (H3 complexity) | ½–1 day | Q4 H3 |
| A-8 | Artifact-size measurement script (API image, frontend bundle raw+gzip — mirror of B's `scripts/ci-artifact-metrics.sh`) | REC | ½ day | Q1 H1 |
| A-9 | Dev-loop benchmark (mirror of B's `task reload-bench`: clean build, incremental build, test wall-clock, edit→reload for `next dev`) | BLOCK (replacement CI metric) | 1 day | Q3 H3 |
| A-10 | Finish `SocialFlowTests` E2E (already owed — CLAUDE.md "Next" item 1) before freezing the benchmark build | REC | 1 session | soundness |
| A-11 | k6 script alignment to J-4 journey mix | BLOCK | ½–1 day after J-4 | Q5 H2 |

Pre-existing planned work interacting with this: the **Redis session**
(`POST-9C-ROADMAP.md` Phase 1) is *not* required for the comparison — see "Conflicts" below.

## Phase U2 — Architecture B proposals (for Hubert; full detail in the B plan)

| ID | Item | Prio | Effort | Serves |
|---|---|---|---|---|
| B-1 | **Deterministic benchmark seed** at J-2 volumes (Postgres path; keep the 24-post demo seed for dev) | BLOCK | 1–2 days | Q5 H2 |
| B-2 | **k6 profiles** matching J-4 (smoke/demo/stress + WS, thresholds, journeys/sec reporting) — extend `loadtest/forum.ts` | BLOCK | 1 day | Q5 H2 |
| B-3 | Align CI Postgres service 16→18 (`.gitlab-ci.yml:201`) | BLOCK | minutes | soundness |
| B-4 | Benchmark-mode limiter config documented (auth limiter `GOMX_AUTH_RATELIMIT` vs. setup logins; equivalent of A's raise-and-restore) | BLOCK | hours | Q5 |
| B-5 | Feed pagination: consider keyset (or document offset + measure at J-2 volumes — offset over 1600 threads is likely fine; over 100k it isn't) | NICE (measure first) | 1–2 days if adopted | Q5 |
| B-6 | Run-archival convention compatible with A's `thesis/results/` layout (meta.json: git SHA, image digest, seed counts, limiter values) | REC | ½ day | reproducibility |
| B-7 | Local build/dev-loop numbers produced with the same protocol as A-9 (B already has `reload-bench`; just align the measured steps + output schema) | BLOCK (paired with A-9) | hours | Q3 H3 |

Explicitly **not** proposed to B: ACL engine, soft-delete/audit columns, outbox/broker
messaging, ULID keys, group chat, peer blocks — leveling those would rebuild B into A. They
stay documented differences (audit §1/§4) covered by the spec's behavioural contract and the
paper's qualitative comparison.

## Phase U3 — Thesis document (detail in `THESIS-REVISED-SECTIONS-DRAFT.md`)

| ID | Item | Prio |
|---|---|---|
| P-1 | Rewrite §3.3 (forum + social domain; why still representative) | BLOCK |
| P-2 | Rewrite §3.2 (actual stacks; PG 18; Tauri on both) | BLOCK |
| P-3 | Revise §3.4 (instruments that exist; journeys/sec; local build metric) | BLOCK |
| P-4 | Revise §3.5 (real environment; drop tc or adopt it jointly; repetition count; delete leftover Polish template text; fix ref [16]) | BLOCK |
| P-5 | Extend §3.7 threats (breadth, KDF, ID/pagination, environment, AI-assisted dev) | REC |
| P-6 | H1 mechanism reword; H2 ceiling definition over journeys; H3 CI-clause replacement | BLOCK |

## What deliberately does NOT get leveled (and why)

- **B-only breadth** (OAuth, email verification, voice notes, comment reviews, i18n, SEO,
  themes): outside the five metric categories; excluded from benchmark journeys; listed in the
  paper as B scope. Building these into A = weeks of busywork with zero measurement value.
- **A-only breadth** (tags, group chat, peer blocks, privacy audiences, admin surface, audit/
  soft-delete): same treatment, mirrored. Hubert already matched the one thing that mattered
  (communities ≈ groups-of-record for the social scope).
- **Messaging backbones** (RabbitMQ+outbox vs Redis pub/sub): kept as-is on both sides; the
  paper gets an explicit architectural comparison and the WS benchmark scenario measures the
  user-visible property (write→receipt latency) rather than the internals.
- **Auth models** (JWT vs sessions): the paradigm itself; kept, with the KDF caveat pinned in
  the protocol.

## Sequencing

1. **Week 1:** J-1…J-7 with Hubert (one working session + async review of forum-spec). In
   parallel on A: A-4 (PG 18), A-6/A-7 (scanners), A-5 (CI).
2. **Week 2:** A-1 (Tauri shell) and A-2/A-3 (Lighthouse/RUM); B-1/B-2 on Hubert's side.
3. **Week 3:** A-9 + B-7 (paired dev-loop measurements), A-8, A-11/B-2 script alignment;
   joint dry-run of the full protocol (one smoke + one demo profile on each side).
4. **Then:** the comparative measured runs (still blocked on Hubert's B-side items landing —
   consistent with CLAUDE.md's "comparative run is blocked" note), and P-1…P-6 finalized from
   the dry-run experience.

## Conflicts with recorded decisions (explicit, per instructions)

1. **Redis on A** (`POST-9C-ROADMAP.md` "Decisions" #1, Phase 1): *no conflict, but a
   re-ordering recommendation.* Redis-on-A is portfolio-driven and performance-neutral by A's
   own measurements. None of the BLOCK items depend on it, and landing it *before* the
   comparative runs slightly changes A's measured surface (an extra hop on cached reads, a
   different rate-limiter). Recommendation: either land Redis **before** the joint dry-run and
   freeze, or **after** the measured runs — not between runs. If the ×replicas rate-limit
   multiplier note matters for the writeup, the distributed limiter is the one Redis piece
   worth landing pre-benchmark.
2. **`PHASE-9-10-ENTERPRISE-PLAN.md` §10d "no Redis" verdict**: unchanged; nothing here
   reverses it (and B's Redis is a WS fan-out bus, not a cache — no scope collision with A's
   planned cache/session/limiter uses; they solve different problems).
3. **POST-9C-ROADMAP Decision #3** ("forum-spec.md blocked on Hubert"): softened, not
   overridden — the spec now *exists as a draft* with `STATUS: DRAFT — pending Hubert's
   review`; sign-off remains blocked on him, but the drafting debt is paid.
4. **CLAUDE.md "do not mirror gomx"**: respected — every B-ward item is framed as leveling
   the *measurement* surface, not copying gomx design into A.

## Suggested updates to living docs (do NOT apply without the user; listed only)

- **CLAUDE.md**: (1) update the "Shared scope contract with B" bullet — the two contract docs
  now exist as drafts in this repo (`docs/specs/forum-spec.md`,
  `docs/architecture/PROPOZYCJA-UJEDNOLICENIA-A-B.md`), pending Hubert's sign-off; (2) add the
  audit/gap/master-plan/backlog documents to the authoritative-docs list; (3) in "Next", add
  the A-side BLOCK items (Tauri shell, Lighthouse, PG 18, scanners, dev-loop bench) alongside
  the existing three; (4) correct the stale phrase "Benchmarked against **Architecture B**
  (colleague's Go SSR monolith…) — the colleague adapts to THIS spec" if desired: the audit
  shows adaptation must now go both ways per the master plan.
- **POST-9C-ROADMAP.md**: add a "Phase 0.5 — A/B unification (BLOCK items)" ahead of the
  Redis/Social-frontend sessions, note the Redis-ordering recommendation above, and mark the
  forum-spec debt as "drafted 2026-07-18, pending sign-off".
- **frontend/README.md**: add the desktop-shell plan (A-1) to the known-gaps list.
