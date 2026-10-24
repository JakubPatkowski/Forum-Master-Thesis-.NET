# A/B test campaign — PROGRESS (resumable checkpoint log)

Campaign folder: `thesis/results/campaign-20261017-2357/` (forum-dotnet repo). Started 2026-10-17 23:57 CEST,
deadline: results presentable at 2026-10-18 08:00. Operator: Claude (Fable 5.1) for Jakub Patkowski.
Rules in force: no git commit/push/stash/checkout/reset; never recreate the minikube profile or wipe MinIO; nothing
heavy on the host during a measured round; every step recorded here with its raw output path.

## Version under test (Phase 0 provenance — `00-preflight/`)
- A: `~/projects/forum-dotnet`, branch `31-feat-phase-13---ab-benchmark`, HEAD `57f63d0` + uncommitted working tree
  (`A-working-tree.patch`, 95 KB incl. the untracked migration `20261017211738_AddOwnerActivityIndexes` and
  `docs/architecture/AB-TEST-RUN-FABLE5-PROMPT.md`; `A-untracked.txt` lists untracked paths).
- B: `~/projects/gomx`, branch `feature/master-thesis`, HEAD `f03d46a` + uncommitted `app/forum/web/handlers_community.go`,
  `handlers_community_test.go`, `web_test.go` (`B-working-tree.patch`, 4.5 KB).
- Harness: `load/k6/main.js` MIX_VERSION `v1`, HARNESS_VERSION `h3-2026-10-17` (`harness-version.txt`).
- Deployed images (`images-and-provenance.txt`): A `forum-dotnet-api:git-57f63d0-dirty` sha256 `a54db7e1…`
  (built 2026-10-17 21:25:31Z = 23:25 CEST), `forum-dotnet-web:git-57f63d0-dirty` sha256 `cd916f5f…` (23:28 CEST);
  B `gomx:bench` = `gomx:bench-f03d46a-dirty` sha256 `80140d5f…` (23:25 CEST), `/stats` reports version `f03d46a-dirty`.
  Every modified source file of both working trees has mtime ≤ 23:17 CEST, i.e. BEFORE the image builds → the images
  contain exactly the archived working trees. No rebuild needed (and none done — a rebuild would only change the digests).
- A migrations applied in the cluster DB `forum_net`: 20 rows in `public.__EFMigrationsHistory`, last =
  `20261017211738_AddOwnerActivityIndexes`; both partial owner indexes present (`ix_threads_owner_activity`,
  `ix_comments_owner_activity`).
- Postgres: `PostgreSQL 18.4 (Debian 18.4-1.pgdg13+1)` on BOTH sides (cluster `postgres-0` in `forum-dotnet` and `gomx`).
- Seed counts at campaign start (NOT pristine — both DBs carry the 23:32 verify round's writes): A 800/1600/9098/15109,
  B 800/1600/9095/15088. `ab-round.sh` reseeds every run to 800/1600/9000/15000 and checks it, so this is expected.
- Common profile: `ab_check_profile.txt` (see Phase 0 result line below).
- Cluster: minikube profile `forum` Running, node Ready (k8s v1.35.1, docker 29.2.1), all pods Running; A backend 2
  replicas (no HPA — bench shape left by the verify round, limiter raised 1000000/1000000, tracing off), B gomx 2 replicas.

## Host state / isolation notes
- Host: 8 vCPU WSL2, 11 GiB RAM, 4 GiB swap (2.8 GiB swap in use at start). VS Code server + several other Claude
  sessions are resident (not mine to kill). The two `dotnet Forum.Api.dll` processes visible in `ps` (uid 1654) ARE the
  cluster's backend pods — never kill by name.
- 00:00 `docker compose stop` (local dev infra postgres/rabbitmq/minio, idle, volumes kept) → restore with
  `docker compose start` when needed (A's Lighthouse script wants the local API + compose infra).

## Mode decision
Phase 3 is expected to start ≈ 01:30 → ≈ 6.5 h remain before 08:00 > 4 h → **ROUND_PLAN = demo:5,stress:1,capacity:1**
(≈ 3 h 15 min). Fallback `demo:3,capacity:1` only if the round cannot start before 04:00.

## Plan with time estimates
| Phase | Content | Est. | Window |
|---|---|---|---|
| 0 | preflight + provenance | 20 min | 23:57 – 00:15 |
| 1 | functional gates A (dotnet build/test TRX/format, npm typecheck/lint/test/build) and B (docker: lint:go, test:go -race, postgres + redis lanes, lint:ts) — in the background | 30–40 min | 00:01 – 00:45 |
| 2 | live checks through the ingress (endpoints, sizes, security posture, WS, health, seed SQL, logs, Prometheus targets, screenshots) | 30 min | 00:45 – 01:20 |
| 3 | gate `smoke:1` (~15 min) then `demo:5,stress:1,capacity:1` (~3 h 15) | 3 h 30 | 01:20 – 04:50 |
| 4 | static metrics H3 (trivy ×2, cloc, artifact sizes, Lighthouse ×2, dependency counts) | 40 min | 04:50 – 05:30 |
| 5 | analysis + CAMPAIGN-REPORT.md + doc updates + index.json + final message | 90 min | 05:30 – 07:00 |

## Step log (append-only; newest at the bottom)
- 23:57 Phase 0 start. Campaign folder created; `git status/log/diff` of both repos saved (`00-preflight/A-*`, `B-*`).
- 23:59 Deployed image tags/digests/creation times, B `/stats`, EF history, owner indexes, file mtimes → `images-and-provenance.txt`.
  Result: images = working trees (see above). No redeploy.
- 00:00 Host: compose stack stopped (memory). k6 v2.1.0, dotnet 10.0.301, node v20.19.6, trivy at ~/.local/bin, headless
  chrome at `.bench/chrome/` present.
- 00:01 Phase 1/A launched in the background (`scratchpad a-functional.sh` → `01-functional/A/{build,test,format,fe-*}.log`,
  TRX in `01-functional/A/trx/`, timings in `timings.txt`).
- 00:03 Phase 1/B launched (`scratchpad b-functional.sh` → `01-functional/B/`): GitLab CI jobs replayed in docker
  (`golang:1.26-alpine` + apk git/bash/gcc/musl-dev/postgresql-client as `gomx-ci-go:local`; `oven/bun:1.3.14-alpine`
  + bash/git as `gomx-ci-bun:local`; postgres:18-alpine + redis:7-alpine services on a throwaway docker network).
- 00:07 Phase 1/A RESULT: build 0 warnings/0 errors (9 s, incremental); `dotnet test` **248 passed / 0 failed / 0 skipped**
  (8 TRX files in `01-functional/A/trx/`, integration suite 46 tests in 4 m 13 s against Testcontainers Postgres+MinIO+RabbitMQ);
  `dotnet format --verify-no-changes` clean; frontend typecheck/lint OK, vitest 69 passed (`fe-vitest-junit.xml`),
  `next build` OK on the first attempt (44 s). Logs: `01-functional/A/*.log`, `timings.txt`.
- 00:07 Phase 1/B first pass: lint:ts GREEN (bun install 909 pkgs, eslint, tsc, prettier), check-generated-go / templ fmt /
  gofmt GREEN; the five Go steps (vet, golangci-lint, 3× go test) died in ≤ 8 s with `permission denied` on
  `~/.cache/gomx-gocache` — the cache is root-owned (written by a root container in an earlier session), the step ran as
  uid 1000. Environment artefact, not a code failure → rerun as root inside the container with identical commands.
- 00:09–00:11 Phase 1/B rerun RESULT (`01-functional/B/*.log`, `*.jsonl`, `timings.txt`): `go vet` clean, golangci-lint
  v1.64.5 clean, `go test -race ./...` **378 passed / 0 failed / 25 skipped** (the 25 = postgres adapter + redis bus tests
  that skip without DATABASE_URL/REDIS_ADDR, exactly like the CI fast lane; total coverage 30.5 %), postgres lane
  **81 passed / 0 failed / 0 skipped** (72.6 %), redis lane 1 passed. B working tree unchanged by the gates (git status
  diff empty). `functional-summary.json` written (A PASS, B PASS). Not replayable here: e2e:playwright + lighthouse CI jobs
  (prebaked Playwright+bun registry image), govulncheck/sonar. Pipeline-failure candidates: lint:ts reproduces GREEN; the
  CI image's go1.26.9 satisfies `go 1.26.1` with GOTOOLCHAIN=local → no toolchain download; MR-lane jobs unverifiable.
- 00:10–00:13 Reseed of both sides to the pristine Benchmark seed (`02-live-checks/reseed-before-live-checks.log`):
  A 800/1600/9000/15000, B 800/1600/9000/15000, fresh pods on both (seed Job + rollout restart on A; DROP/CREATE + boot
  seed + pod replacement on B).
- 00:13–00:14 Phase 2 live checks (`scratchpad live-checks.sh` → `02-live-checks/endpoints.tsv`, `raw/`, `live-checks.log`,
  `tls.txt`, `health.txt`, `limiter-and-runtime-env.txt`, `seed-shape-{A,B}.txt`). Headline: every harness endpoint
  answers as the adapters expect on both sides; A private thread/feed/comments → 404 anonymously, 0 private rows in
  anonymous search (268 private threads in DB); A WS 101 with a fresh ticket, 401 on replay; B login 303 + HttpOnly/Secure
  cookie, J3 303 → `#comment-9001`, `/auth/google` 303 → `/login?error=oauth_unavailable` without a session cookie,
  WS 101 with the cookie — AND 101 anonymously (B accepts unauthenticated sockets; noted, not measured).
- 00:05 FINDING (A SPA, functional, not in the harness path): the anonymous home page `/` renders "SIGNAL LOST — Category
  not found": `useHomeFeed` merges every category from `GET /api/content/categories` (which returns all 12 incl. the 4
  private ones) and one 404 from a private feed (the A-12 read gate, 2026-10-17) rejects the whole `Promise.all` →
  error state. Per-category pages, thread, search and profile work. Regression introduced by A-12; fix candidate: treat
  404/403 per source as "exhausted" in `use-home-feed.ts` (or merge only public categories for anonymous users). NOT fixed
  before the round (would change the deployed web image; the frontend pod idles during measurements). To do in Phase 5.
- 00:14–00:15 Screenshots (`02-live-checks/screenshots/`, manifest.tsv): A home/feed/thread/search/profile + B same five pages, all rendered
  with real Benchmark content (A home = the documented "SIGNAL LOST" regression; the other A pages fine).
- 00:16–00:18 SQL statements per journey on the live DBs (`02-live-checks/sql-per-journey/`): `log_statement=all` window on both
  Postgres pods, one request per journey with psql markers, logging RESET and verified `none` on both afterwards (run.log).
  First attempt failed (`ALTER SYSTEM cannot run inside a transaction block` — psql `-c` with two statements) and changed
  nothing; the fixed script is the recorded run. Foreground counts reproduce AB-COMPARISON-v1 §5.6; B nav = 1 statement
  (anonymous-membership fix confirmed live). Idle DB chatter: A 188 vs B 116 statements per 20 s (2 replicas each).
- 00:19 `02-live-checks/SUMMARY.md` written (endpoint table, posture findings, SQL table, screenshot list). Phase 2 COMPLETE.
- 00:21 Phase 3 gate started: `make ab-round ARGS="--plan smoke:1 --label gate"` (background; stdout →
  `03-round/gate-stdout.log`; archive → `thesis/results/rounds/<stamp>-gate/`). Host idle: compose stopped, no test
  containers, load avg 1.3, 4.0 GiB available, swap use unchanged (VS Code + other sessions resident).
- 00:20–00:28 Phase 3 GATE RESULT: `thesis/results/rounds/20261018-0019-gate/` — smoke A valid, smoke B valid, 0.00 % failed
  on both, journeys/s 10.00 vs 9.87, p95 14 vs 30 ms, app CPU/journey 11.2 vs 4.6 ms (marginal 10.5 vs 4.3), k6 ≤ 16 % of
  one core. stdout: `03-round/gate-stdout.log`.
- 00:29 Phase 3 MEASURED ROUND STARTED (background): `make ab-round ARGS="--plan demo:5,stress:1,capacity:1 --label v1"`,
  stdout → `03-round/round-stdout.log`, archive → `thesis/results/rounds/20261018-0029-v1/` (stamp = start minute; confirm
  from the stdout log's "archive:" line). Expected end ≈ 03:45. Host: compose stopped, no containers, no tunnels, nothing
  else started by this session. RULE: nothing heavy on the host until the round finishes. If this session dies: the round
  keeps running (nohup via the Bash tool); a new session must NOT start another round — check `pgrep -f ab-round.sh`,
  then read the archive's REPORT.md when `round.log` ends with "round complete".
- 00:45 (discovered 00:51) **INCIDENT: the Claude Code session process exited and took its background tasks with it** —
  `make ab-round` (started 00:29) died during `[demo 1] B` (k6 scenario running, A scaled to 0). Demo A run-1 had completed
  valid; B run-1 has only its warm-up. Partial archive renamed to `thesis/results/rounds/20261018-0029-v1-ABORTED-session-crash/`
  (kept for forensics, NOT used in the report); its stdout → `03-round/round-stdout-ABORTED-0029.log`. The limiter-restore
  trap did not run (killed hard) — harmless, `ab_apply_profile` re-applies the profile at every round start.
  LESSON: a Bash-tool background task does not survive a session restart → the round is relaunched DETACHED
  (`setsid nohup … &`, own session id) so only the host can kill it; a new session resumes by reading the archive's round.log.
- 00:52 Phase 3 MEASURED ROUND RE-STARTED (detached): `make ab-round ARGS="--plan demo:5,stress:1,capacity:1 --label v1"`,
  stdout → `03-round/round-stdout.log`, archive → `thesis/results/rounds/20261018-0051-v1/` (detached: SID 874509, pid 874511).
  Expected end ≈ 04:10. Time budget still allows the full plan (Phase 3 start at 00:52 → 7 h before 08:00).
- 03:43 Phase 3 MEASURED ROUND COMPLETE: `thesis/results/rounds/20261018-0051-v1/` — **14/14 runs valid** (demo 5+5, stress
  1+1, capacity 1+1), 0 restarts, 0 × 429, 0.00 % failed everywhere. REPORT.md + round-summary.json + meta/round.json copied to
  `03-round/`. Headline (REPORT.md): demo journeys/s 91.6 vs 90.1 (closed model); journey p95 19.2 ± 3.1 vs 60.8 ± 18.3 ms
  (distinct); app CPU/journey 5.02 ± 0.29 vs 4.84 ± 0.36 ms (overlap), marginal 4.88 vs 4.77 (overlap); DB CPU/journey 2.69 vs
  4.55 (distinct, B 1.69×); bus 1.52 vs 0.14; whole-namespace CPU/journey 9.25 vs 9.54 (overlap), marginal 7.88 vs 9.32
  (overlap); whole-namespace idle cores 0.125 vs 0.021; app memory 390 vs 66 MiB; wire 2.21 vs 11.63 KiB/journey. Stress: p95
  41 vs 454 ms, journeys/s 190 vs 156. Capacity (H2): sustained A 300 j/s (knee c07 @ 400 offered, p95 595 ms) vs B 200 j/s
  (knee c06 @ 300 offered, p95 3.2 s; achieved plateaus at ≈ 205 j/s from c06 on) — knee reached on BOTH sides, no rerun needed.
  k6 headroom: max 127 % of one core (A capacity) on an 8-core host, node busy avg ≤ 4.3 cores; k6 RSS up to 2.3 GiB (A capacity).
  Anomaly to analyse: B WebSocket holds report errors under load (stress 148, capacity 152; A 0) and more "subscribed" than
  sockets (reconnects).
- 03:46 Phase 4 static metrics launched DETACHED (`scratchpad static-metrics.sh` → `04-static/`, timings.txt): trivy ×2, cloc ×4,
  artifact/image sizes, dependency counts, Lighthouse B (docker image, demo seed) then Lighthouse A (compose + local API + next
  start). Round-window logs/metrics collection (Loki/Prometheus) → `03-round/logs-round-window/`.
- 03:47 FIX (A frontend, working tree only — NOT in the measured image): `frontend/src/lib/feed/feed-merge.ts` gains
  `exhaustSource()`; `use-home-feed.ts` catches `ApiError` 404/403 per category feed and exhausts that source instead of
  failing the whole merge (the anonymous `/` regression from A-12). New test `feed-merge.exhaust.test.ts` (2 tests);
  `tsc --noEmit` clean, vitest 71 passed, `next lint` clean. Patch archived as `05-analysis/A-frontend-home-feed-fix.patch`.
  The fix landed before Phase 4's Lighthouse-A `next build`, so the local Lighthouse audit of `/` runs on the fixed home page
  (the deployed `forum-dotnet-web:git-57f63d0-dirty` image still carries the bug — rebuild with `make mk-deploy` when restoring
  the production posture).
- 03:48 Round-window log analysis (`03-round/logs-round-window/summary.json`, Loki): A backend 64 warning lines = the 4 known
  startup warnings × 16 fresh pods (DataProtection ephemeral keys, HTTP_PORTS override), 0 errors, **0 × 5xx** over 738 365
  request lines; seed Job pods print `Error: libgssapi_krb5.so.2 …` (Npgsql's Kerberos probe in the distroless image, harmless,
  7×); B gomx 0 error/warn lines, **8 × 5xx** over 376 931 request lines (see `B-5xx.log` — classified below); B Postgres
  `relation "goose_db_version" does not exist` once per reseed (fresh DB, benign). Node memory (minikube VM = WSL kernel): swap use
  flat at ≈ 1.5 GiB (pre-existing, Δ ≤ 40 MiB during any run), MemAvailable min 1.2 GiB during A's capacity run (k6 RSS 2.3 GiB).
- 03:50 Phase 4 COMPLETE (`04-static/timings.txt`): trivy A rc=1 (findings) / B rc=1, cloc ×4 (first pass produced empty
  JSON — `--vcs=git` needs the repo as cwd; rerun 03:50 from inside each repo with cloc 2.06), artifact sizes, image sizes,
  dependency counts, Lighthouse B (113 s), Lighthouse A (171 s). `STATIC-SUMMARY.md` + `static-summary.json` written with the
  2026-10-17 comparison. Not run: dev-loop (B toolchain absent), Lighthouse vs cluster (v2).
- 03:52–03:58 Round-window analysis: `03-round/capacity-per-step-cpu.json` (Prometheus per step: B's Postgres at 1.00 core from
  c06, A's at 0.93 from c07 — the H2 knee is the 1-CPU DB on both sides); B's hub drops slow WS consumers by design
  (`hub.go` "client too slow to keep up") → the 148/152 WS errors under stress/capacity; B's 8 × 500 at 00:49:39 = cancelled
  requests of the aborted round's orphaned k6 ("canceling statement due to user request"), outside all windows.
- 03:58 `05-analysis/CAMPAIGN-REPORT.md` written in full (§0–§8). `AB-COMPARISON-v1.md` §9 filled verbatim from REPORT.md.
- 04:00 PROVENANCE CORRECTION for Lighthouse A: `docker compose start` had also started the compose `api` container (image
  `forum-dotnet-api` built 2026-07-18, pre-A-12) on :8080, so the working-tree API in `dev-api.sh` crashed on bind and the
  03:47 audit ran against that OLD API (`04-static/lighthouse-A-attempt1-old-compose-api/`, kept). Rerun in progress against
  the working-tree API (`docker compose stop api` first; `lighthouse-A-rerun.log`); the home-fix verification screenshot is
  taken in the same pass (`05-analysis/A-home-after-fix-local.png`). Earlier verification attempts killed the operator shell
  (pattern kills matching the shell's own command line) and are void.
- 04:01–04:03 Lighthouse A RERUN against the working-tree API (`04-static/lighthouse-A-rerun.log`: API Healthy, anonymous private
  feed → 404 confirmed, `lighthouse.sh` 120 s): home perf 0.84 / thread 0.81 → recorded as `lighthouse-A/` +
  `lighthouse-metrics-A.txt`; attempt 1 (old compose API, 0.75/0.77) kept as `lighthouse-A-attempt1-old-compose-api/`.
  Home-feed fix VERIFIED on the same local stack: `05-analysis/A-home-after-fix-local.png` renders the merged feed with the
  private category present in the sidebar (before: `02-live-checks/screenshots/A-home.png` error state). Local stack torn down
  by pid files (ports 8080/3000 free, compose stopped). B artifact sizes re-extracted from the correct image paths
  (`/app/server` 39.9 MB raw / 13.2 MB gzip; css+js 145 250 B gzip). `04-static/STATIC-SUMMARY.md` + `static-summary.json`
  regenerated; CAMPAIGN-REPORT §4/§6 updated accordingly.
- 04:05 Documentation updated: `thesis/results/AB-COMPARISON-v1.md` §9 (verbatim tables from REPORT.md) + §12 (DB knee, WS
  loss); `docs/architecture/AB-START-HERE.md` (state table, findings log ×5, §4.4 open items ×2, traps 21–26); `CLAUDE.md`
  Current state/Next; memory `ab-campaign-2026-10-18.md` + MEMORY.md index. gomx `deploy/k8s/bench/README.md` unchanged
  (B's procedure did not change).
- FINAL STATE: cluster `forum` up, both apps deployed and scaled to 2 (bench shape: no HPA, A limiter STILL RAISED to 1 000 000 permits — ab-round's trap restored the value it found, which was already the raised
  one from the earlier bench sessions — tracing off; `make mk-deploy` restores production posture and would also deploy the web image with the home-feed fix);
  both DBs carry the last capacity run's writes (reseed before any new measurement — ab-round does it); local compose stack
  stopped (`docker compose start` restarts it — note it also starts the old `api` container); no stray local processes.
  Nothing committed (owner commits). Campaign COMPLETE except `index.json` (generated last).
