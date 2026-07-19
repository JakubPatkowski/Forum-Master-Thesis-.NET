# Unification Backlog — Architecture A (forum-dotnet)

_Written 2026-07-18. Standalone implementation backlog for the A-side items of
`AB-UNIFICATION-MASTER-PLAN.md`, in the same delegate-to-a-dedicated-session pattern as the
Redis/Social sessions in `POST-9C-ROADMAP.md`. Each item is self-contained enough to hand to
a fresh session together with this file + the master plan._

_Ordering below = recommended execution order, not priority order; priorities repeated from
the master plan (BLOCK / REC / NICE)._

---

## A-4 — PostgreSQL 17 → 18  (BLOCK, ~½ day)

**Why:** J-7 — same DB major version on both sides; B already deploys `postgres:18-alpine`
(`gomx/deploy/k8s/postgres.yaml`) and the thesis §3.2 says 18.

**Scope:**
- `compose.yaml:4` `postgres:17` → `postgres:18`; `k8s/postgres/statefulset.yaml:28` same.
- Testcontainers: check the image tag used by `PostgresFixture`
  (`backend/tests/Forum.TestUtilities/`) and bump to the same major.
- Local dev DBs must be dumped/recreated (PG major upgrade ≠ in-place with the official
  image; the compose volume `pgdata` and the k8s PVC both need a reset — dev/bench data is
  reseedable by design, so `make reset-db` / reseed is the path).

**Acceptance criteria:**
- Full `dotnet test` green (esp. `AclSqlTests`, FTS tests, `SeedFlowTests` — they exercise
  raw SQL most likely to notice engine changes).
- `make mk-deploy ARGS=--seed` clean on a fresh cluster; `/health/ready` green.
- Grep confirms no remaining `postgres:17` reference (docs updated too).

## A-6 — trivy CVE scan parity  (BLOCK, ~½ day)

**Why:** J-5 / H3 — CVE counts are only comparable from the same scanner; B runs
`trivy fs --scanners vuln,secret,misconfig` in CI (`gomx/.gitlab-ci.yml:266-301`).

**Scope:**
- `scripts/security-scan.sh`: portable trivy binary (memory note: no trivy on this box, no
  sudo — install to `~/.local/bin` like k6), running the same flags as B: severity
  HIGH,CRITICAL, `--ignore-unfixed`, JSON artifact + table output, over the repo root.
- Optional GitHub Actions job mirroring `security.yml`'s schedule.

**Acceptance:** script produces `trivy-report.json`; a documented one-liner exists to run the
identical scan against a gomx checkout (read-only) so the same-day paired scan (J-5) is one
command per repo.

## A-7 — SonarQube (complexity metric parity)  (BLOCK, ~½–1 day)

**Why:** J-5 / H3 cyclomatic complexity; B has `sonar-project.properties` and a token-gated CI
job.

**Scope:**
- `sonar-project.properties` (or `dotnet-sonarscanner` invocation script) covering
  `backend/src` + `frontend/src`.
- Exclusions mirroring B's philosophy (B excludes `*_templ.go`, `postgres/db/`,
  `static/`): exclude EF `Migrations/` folders (already `generated_code=true` in their
  `.editorconfig`), `frontend/.next/`, `design-reference/`, generated OpenAPI clients if any.
- Server: same SonarQube instance/version the B side uses (coordinate with Hubert — J-5) or
  a local `sonarqube:community` container run on scan day.

**Acceptance:** one command produces a complexity report per repo; measurement protocol
(forum-spec §9) names the Sonar version.

## A-5 — Frontend + image in CI  (REC, ~½ day)

**Why:** Q3 honesty — `.github/workflows/ci.yml` currently builds only the backend; the
frontend has zero CI. Also prerequisite for reporting A's pipeline scope fairly (J-6).

**Scope:** extend `ci.yml` with a second job: `npm ci && npm run typecheck && npm run lint &&
npm test && npm run build` in `frontend/`; a third job building the two Docker images (no
push; hadolint optional). Keep it one workflow — A's pipeline stays honest-sized, we are NOT
padding it to look like B's.

**Acceptance:** PR CI runs backend + frontend + image build; README badge/notes updated.

## A-2 — Lighthouse harness  (BLOCK, ~1 day)

**Why:** Q2/H2 — the paper's stated CWV instrument; B has it in CI + locally
(`gomx/lighthouserc.json`, `scripts/lighthouse.sh`). A currently has no CWV data source.

**Scope:**
- `frontend/lighthouserc.json` + `scripts/lighthouse.sh`: audit `/` (feed) and one `/t/[id]`
  thread page (mirror B's two-page choice: `gomx/lighthouserc.json` audits `/` and
  `/thread/1`), 3 runs each, report-only budgets identical to B's numbers (perf .9, a11y 1,
  LCP 2500, CLS .1, TBT 200, FCP 1800) so both sides' reports read against the same bar.
- Target: production build (`next build && next start`) against the live compose/cluster API
  with the seeded Development dataset; document that in the script header (a CSR SPA audited
  without a live API renders skeletons — the API must be up, unlike B's self-contained
  server).
- Thread id: deterministic from the Development seed (SeedUlids are reproducible — resolve
  one thread id via the API in the script rather than hard-coding).

**Acceptance:** `scripts/lighthouse.sh` produces `.bench/lighthouse/*.report.html` + a metrics
summary file with LCP/CLS/TBT/FCP/scores; runbook section added; protocol (forum-spec §9)
updated with the paired-run instructions.

## A-3 — RUM Web Vitals beacon  (REC, ~1–2 days)

**Why:** Q2 field data; B beacons LCP/FCP/INP/TTFB/CLS from real browsers into Prometheus
(`gomx/internal/metrics/metrics.go:137-147`, `shared/bridge.ts`).

**Scope:**
- Frontend: `web-vitals` package, beacon via `navigator.sendBeacon` to a new endpoint.
- Backend: minimal `POST /api/telemetry/vitals` endpoint in `Forum.Api` (Bootstrap — host
  wiring like the realtime hub, not a module) recording into `ForumMetrics` histograms
  mirroring B's: seconds histogram labelled `{metric, rating}` + separate unitless CLS.
  Guard cardinality exactly as B does (allow-list metric names + ratings); cap body size;
  anonymous + rate-limited (A's global limiter covers it; note B uses a dedicated
  120/min/IP beacon limiter — `gomx/internal/server/router.go:150`).
- Add the two series to `OBSERVABILITY-CONTRACT.md` and one Grafana panel (dashboards live in
  `k8s/monitoring/grafana-dashboards/`).

**Acceptance:** browsing the SPA against the cluster produces `web_vitals_*` samples on
`/metrics`; contract tests updated; ObservabilityFlowTests extended with one beacon POST.

## A-1 — Tauri v2 desktop shell + mobile note  (BLOCK, ~3–5 days)

**Why:** the immutable title ("Cross-Platform Applications") and H1's desktop half currently
have no A-side object; B ships a working Tauri v2 desktop app (`gomx/src-tauri/`). Same shell
technology on both sides holds the shell constant (gap analysis §T).

**Scope:**
- `desktop/` (or `frontend/src-tauri/`): Tauri v2 project, webview loading the SPA.
- **Decision 1 — content source.** Two viable modes; implement (a), document (b):
  (a) *bundled static export*: `next build` with `output: 'export'`; A is pure CSR so this is
  feasible, but dynamic routes (`/t/[id]`, `/u/[userId]`, `/c/[slug]`) need the SPA-fallback
  pattern (export a single shell + client routing; verify Next 15 static export constraints —
  if `generateStaticParams` friction is too high, a hash-router shim or the hosted mode wins);
  (b) *hosted URL mode*: `frontendDist` → deployed origin, mirroring B's mobile Option 1
  (`gomx/MOBILE.md`). If (a) fights Next.js, ship (b) and say so — an honest thin-client shell
  beats a hacked export.
- **Decision 2 — API base URL**: config file/env override surfaced in the shell (the SPA
  already reads `NEXT_PUBLIC_API_URL`; for the bundled mode inject at build time, and note the
  cookie/CORS implication: the refresh cookie needs the API origin in the CORS allow-list and
  `SameSite=None; Secure` when the webview origin is `tauri://` — verify against
  `Forum.Api`'s CORS/cookie setup early, this is the likely dragon).
- No sidecar, no offline mode — and `A-MOBILE.md` documenting that + the mobile path (hosted
  shell, same as B's chosen option), so the two repos' cross-platform claims are symmetric.
- Measurement hooks (H1): document how to read the desktop app's RSS/CPU (per-platform:
  Task Manager/`ps` on the webview + shell processes) and installer size; add both to the
  benchmark protocol.

**Acceptance:** `task`/npm script builds a runnable desktop app on Linux (WSLg caveat noted)
and a Windows installer; login → feed → thread → comment → live WS update works inside the
shell against the cluster; `A-MOBILE.md` exists; README updated.

## A-9 — Dev-loop benchmark harness  (BLOCK, ~1 day)

**Why:** J-5/J-6 replacement for the PDF's CI-timing metric; must be protocol-identical to
B's `task reload-bench` (`gomx/scripts/reload-bench.sh`, JSON output
`.bench/reload-bench.json`).

**Scope:** `scripts/dev-loop-bench.sh` measuring, N=10 runs each with fresh-edit injection
(B's trick: touch a real source file so caches actually re-do work, restore tree on exit):
1. backend clean build (`dotnet build` after `dotnet clean` — plus a documented
   cold-restore variant), 2. backend incremental build after a one-line edit,
3. `dotnet test` wall clock (documented: includes Testcontainers boot; report unit-only and
   full separately), 4. frontend clean `npm run build`, 5. frontend incremental (dev-server
   HMR latency — measure via timestamp log same as B's air-chain timing), 6. `next dev`
   cold start.
Output schema: same JSON shape as B's `.bench/reload-bench.json` so the thesis table is one
join.

**Acceptance:** one command → table + JSON; paired protocol note in forum-spec §9.

## A-8 — Artifact-size measurement  (REC, ~½ day)

**Why:** H1 size metrics; B publishes binary/bundle/image sizes per MR
(`gomx/scripts/ci-artifact-metrics.sh`, `image-size-metrics.sh`).

**Scope:** `scripts/artifact-size.sh`: API image size, frontend image size, frontend
first-load JS (parse `next build` output) raw+gzip, desktop installer size (after A-1).
OpenMetrics-style text output to match B's schema.

## A-11 — k6 journey alignment  (BLOCK, ~½–1 day after J-4)

**Why:** J-4 — the measured mixes must express the same user behaviour.

**Scope:** map the agreed journey mix onto `load/k6/main.js` (mostly re-weighting the
existing 9-action mix + tagging requests by journey so summaries report journeys/sec);
keep A-specific mechanics (ticket→WS, presigned upload) — they ARE the paradigm. Export the
per-journey summary block both sides' reports share (schema into forum-spec §9).

## A-10 — SocialFlowTests E2E  (REC, 1 session — already specced)

Unchanged from `PHASE-11-SOCIAL-PROGRESS.md` item 17 / CLAUDE.md "Next" #1. Listed here only
because the benchmark build should be frozen with the suite green, and the frozen SHA recorded
in meta.json.

---

## Explicitly rejected for A (do not build)

- **i18n / OAuth / email verification / voice notes / SEO endpoints** — B-side breadth outside
  the metric categories (master plan "not leveled" list).
- **Electron or Capacitor shell** — confounds shell weight with architecture (gap §T).
- **React-Query persister, offline desktop mode** — conflicts with presigned-URL TTLs
  (recorded 10d decision) and out of scope for a thin client.

## Suggested session split (Fable 5 pattern, one session each)

1. "A-side measurement parity" — A-4, A-6, A-7, A-5, A-8 (mechanical, low-risk).
2. "Lighthouse + RUM" — A-2, A-3 (touches frontend + Bootstrap + contract docs).
3. "Tauri desktop shell" — A-1 (own session; the CORS/cookie dragon needs focus).
4. "Bench harness v2" — A-9, A-11 (after J-4 lands with Hubert).
