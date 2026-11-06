# A/B test campaign v3 — PROGRESS (resumable checkpoint log)

Campaign folder: `thesis/results/campaign-20261024-2129/` (forum-dotnet repo). Session started 2026-10-24 21:10 CEST;
deadline: results presentable at 2026-10-25 08:00. Operator: Claude (Opus 5.5) for Jakub Patkowski.
Prompt: `~/ab-campaign-v3-prompt.md` (outside the repo on purpose — an untracked file would make the image tag `-dirty`).
Rules in force: no git commit/push/tag/stash/checkout/reset/clean; NO code changes (the tags ARE the version under test —
a bug found tonight is a RESULT); never recreate the minikube profile, `make mk-down`, wipe PVCs/MinIO; nothing heavy on the
host while the gate or the measured round runs; every step recorded here with its raw-output path.

## Version under test (Phase 0 — `00-preflight/`)
- A: `~/projects/forum-dotnet`, branch `31-feat-phase-13---ab-benchmark`, tag **`thesis-v3-a` = `64cd5e2`** (committed
  2026-10-24 20:09:43 CEST), working tree clean (`A-git-status.txt`, `rule4-clean-build-proof.txt`).
- B: `~/projects/gomx`, branch `feature/master-thesis`, tag **`thesis-v3-b` = `46b342c`**, clean (`B-git-status.txt`).
  The remote tag on GitLab could NOT be verified from this shell (no GitLab credentials; the branch tracks origin with
  0 ahead, so the commit itself is pushed) — owner to check `git ls-remote --tags origin | grep thesis`.
- Harness: `load/k6/main.js` MIX_VERSION `v2`, HARNESS_VERSION `h6-2026-10-24`; k6 v2.1.0 (`harness-version.txt`).
- Deployed images (`images-and-provenance.txt`): A `forum-dotnet-api:git-64cd5e2` sha256 `2f1cde21…`,
  `forum-dotnet-web:git-64cd5e2` sha256 `3056a462…`; B `gomx:bench` = `gomx:bench-46b342c` sha256 `345b7cba…`, `/stats`
  version `46b342c` (no `-dirty` anywhere). Both deploy scripts build INSIDE minikube's docker daemon (`minikube
  docker-env`) — the host-docker images built at ~20:25 CEST were not used (correction to the prompt's assumption); the API
  image's `created` timestamp (20:22 CEST) comes from an earlier, terminated `mk-deploy` of the SAME clean commit whose
  BuildKit cache tonight's build reused step-for-step (note at the end of `images-and-provenance.txt`).
- A migrations: 16 rows in `public.__EFMigrationsHistory`, last two `20261024001000_AddFeedExcerpt`,
  `20261024002000_OptimizeEffectiveMask`; functions `forum_content.thread_excerpt`, `forum_authz.effective_mask`,
  `forum_authz.has_permission` present.
- Postgres: `PostgreSQL 18.4 (Debian 18.4-1.pgdg13+1)` on BOTH sides (image `postgres:18`, same digest `3a82e1f5…`).
- B overlay (`B-bench-overlay-checks.txt`, rendered YAML kept): 0 HPA, 0 `pool_max_conns`, `GOMX_DB_MAX_CONNS=30`,
  `GOMX_CARD_PREVIEW_CHARS=300`, `GOMX_SEED=bench`, `GOMX_COMPRESS=0`, `GOMX_AUTH_RATELIMIT=0`, `GOMX_NOTIFY_FRIENDS_ON_POST=0`.
- Rule-4 caveat: `meta-A.json` `git_dirty` will read `true` during the round (the round archive itself is untracked);
  the clean-build proof is `rule4-clean-build-proof.txt` (porcelain outside `thesis/results` empty, `git diff --quiet HEAD`
  exit 0, `git describe --tags --exact-match` = `thesis-v3-a`) + the digests above.

## Host / cluster state
- Host: 8 vCPU WSL2, 11 GiB RAM; minikube profile `forum` (8 CPU / 10 GiB, docker driver) was Stopped → `make mk-up` 21:12,
  all pods Ready 21:14:46; `make mon-check` green 21:15 (7/7 targets, Loki, Tempo).

## Mode decision
Phase 3 is expected to start ≈ 22:05 (well before the 00:30 cut-off) → **ROUND_PLAN = demo:10,capacity:1** (≈ 4 h 45 min,
expected end ≈ 02:50). N = 10 per side gives an exact two-sided Mann-Whitney floor of p = 1.1e-5 (passes Holm over the
whole metric family); N < 6 is never planned.

## Plan with time estimates
| Phase | Content | Est. | Window |
|---|---|---|---|
| 0 | cluster start, deploys from clean tags, provenance, profile, seed counts, word bank | 25 min | 21:10 – 21:35 |
| 1 | live checks through the ingress (endpoints incl. J4/J9/J8, posture, TLS, gzip, health, env, targets, logs, seed shape, export dry run) | 20 min | 21:35 – 21:55 |
| 2 | gate `smoke:1 --label v3-gate` (detached) + validity read | 15 min | 21:55 – 22:10 |
| 3 | measured round `demo:10,capacity:1 --label v3` (detached) → immediately: Prometheus/Loki/events export, figures | 4 h 45 + 15 min | 22:10 – 03:10 |
| 4 | functional gates on fresh clones of both tags, SQL per journey on a pristine reseed, static H3 (trivy, cloc, sizes, deps, Lighthouse cluster), screenshots | 1 h 30 | 03:10 – 04:40 |
| 5 | CAMPAIGN-REPORT, ARTICLE-RESULTS-DIGEST, AB-COMPARISON v3 section, AB-START-HERE/CLAUDE.md/memory, index.json, backup | 1 h 15 | 04:40 – 06:00 |

## Step log (append-only; newest at the bottom)
- 21:10 Session start; prompt file read (181 lines = the pasted prompt). Both repos clean, HEADs = tags (`thesis-v3-a` 64cd5e2,
  `thesis-v3-b` 46b342c).
- 21:12 `make mk-up` → cluster Running (`00-preflight/00-mk-up.log`); 21:14:46 every pod Ready; 21:15 `make mon-check` green
  (`00-preflight/00-mon-check.log`).
- 21:15–21:19 `make mk-deploy ARGS=--seed-benchmark` (porcelain empty right before; `00-preflight/01-deploy-A.log`): tag
  `git-64cd5e2`, migrate + Benchmark seed Jobs complete, backend/frontend rolled. rc=0.
- 21:20–21:28 `make gomx-deploy ARGS=--reset-db` (gomx porcelain empty right before; `00-preflight/02-deploy-B.log`): image built
  in minikube, DB dropped/recreated, bench seed, scaled to 2, ServiceMonitor; `/stats` = 1600 posts / 9000 comments / `46b342c`. rc=0.
- 21:29 Campaign folder created (only now — clean-tag rule); deploy logs copied; provenance written (`00-preflight/*`).
- 21:30 Common profile: `ab_apply_profile a; ab_apply_profile b; ab_check_profile a; ab_check_profile b` → **38 ok / 0 FAIL**
  (`00-preflight/ab_check_profile.txt`). Seed counts A `800 1600 9000 15000 2000` = B = expected (`seed-counts.txt`).
- 21:30–21:33 Search word bank (`00-preflight/search-bank.txt`, `search-bank-full.tsv`): rank 1 abandon A 1600 / B 1600,
  rank 50 beacon 614 / 613, rank 100 cousin 331 / 295 (B/A 0.891 — misses the ±10 % check by 0.9 pp; identical to the
  2026-10-18 counts → deterministic seeds, two different PRNGs), rank 200 midnight 146 / 141, rank 300 zephyr 97 / 96.
  Whole bank (300 words): public J6 match sets A 90 656 vs B 91 371 rows (B/A 1.008; 302 vs 305 per query); per-word
  |B/A − 1| median 4.9 %, max ≈ 40 % (peacock 143/92, hamlet 174/225) — recorded as a J6 caveat, not a blocker.
- 21:32–21:35 Phase 1 live checks (`scratchpad live-checks.sh` → `02-live-checks/endpoints.tsv`, `raw/`, `live-checks.log`,
  `ids.txt`, `tls.txt`, `health.txt`, `limiter-and-runtime-env.txt`): every h6 endpoint answers as the adapters expect on
  both sides — A J4 initiate 201 → presigned PUT 200 to `https://minio.forum.local` → commit 200 → thread 201 → attach 204;
  A J9 friends 6 → open 200 → send 200; B J4 upload 202 → status completed after 1 poll → `/posts` 303 `/thread/1601`;
  B J9 303 `/messages/28` (no `?error=rejected`). Posture: A anonymous categories list 0/8 private, private thread / feed /
  comments / category-by-slug → 404, 0 private rows in anonymous search; B `/auth/google` 303 `/login?error=oauth_unavailable`
  without `forum_session`, private space/thread 404. Card sizes (v3): A feed page 14 264 → 3 775 B gzip (20 × 300-char
  `excerpt`; v1 8 004 → 1 565), B space page 144 048 → 15 435 B (v1 174 116 → 16 205) — the excerpt text is equal, the byte
  gap halved (gzip B/A 7.9× → 3.6× incl. A's batch) but remains: B's card MARKUP ≈ 6.4 KB raw per card (HTML vs JSON paradigm).
- 21:33–21:34 WS: first-pass capture artefact (curl `-i` does not flush a 101's headers when no frame arrives before
  `--max-time`) → re-checked with `curl -v`: A 101 with a fresh ticket, B 101 with cookie AND anonymously. **Finding: A's
  ticket single-use is per replica** — one ticket, 4 handshakes → `101 101 401 401` (twice): one socket per replica within
  the 30-s TTL (documented Phase 7 design: in-memory redeemed-jti cache; v1's single replay hit the same replica).
- 21:34 **B private-comment WS gate live test** (`02-live-checks/b-ws-private-gate/`, k6): member of private `bench12`
  (`benchuser0012`) comments on private post 5 and public post 1 → anonymous socket 0 / non-member (`benchuser0002`) 0 /
  member 1 private-comment frames; public comment reached all three → **PASS** (thesis-v3-b gate works live).
- 21:35–21:37 Prometheus 19/19 targets UP incl. both gomx pods (`prometheus-targets.txt`); logs before the round
  (`logs-before-round/`, SUMMARY.txt): A = known startup warnings + **environmental incident at 21:25 CEST**: during B's
  image build (Go compile + bun install on the shared node) A's RabbitMQ failed its exec liveness probe
  (`rabbitmq-diagnostics`, 10 s) and restarted (restartCount 12), A Postgres probes timed out once, backend pod fl727 logged
  broker loss + `/health/ready` 503 for ~1 min and reconnected 21:26:28 — before any measurement (the round builds nothing;
  every run restarts A's app pods). B: only the expected OAuth-refusal WARNs from the posture probe. Seed shape
  (`seed-shape-{A,B}.txt`) taken after the live-check writes (+2 comments, +1 thread, +1 DM per side; pristine re-check in
  Phase 4). Post-round export script dry-run on the last 20 min OK (102 PromQL queries, 0 errors; Loki streams readable).
- 21:37 Phase 2 gate launched DETACHED: `setsid nohup make ab-round ARGS="--plan smoke:1 --label v3-gate"` → archive
  `thesis/results/rounds/20261024-2137-v3-gate/` (stdout `03-round/gate-stdout.log`); waiting with ONE background until-loop
  (`scratchpad wait-round.sh`, 120 s, exits on state ≠ RUNNING or heartbeat > 15 min). Host otherwise idle.
- 21:40 Gate smoke A finished: `valid=True`; write_check comments 68/68, threads 21/21, DMs 21/21; core p95 22 ms;
  **2 failed requests = `dm_open` 404** (13 ok / 2 failed). Diagnosed (Loki: 4 × 404 on `POST /api/social/conversations/direct`,
  2 in the warm-up, 2 in the run; SQL): **finding D17 — A's Benchmark social graph (`BenchSocialGraph`, ring + chords over all
  800 users) gives 26 of the 1 200 friend pairs of the login pool (`bench_user_0034…0233`) a BLOCKED friend
  (`bench_user_0013…0032`, an A-only seed band); `GET /api/social/friends` lists them, the h6 adapter's J9 picks a random
  friend, and A's open-direct answers 404 (non-active accounts are socially invisible) — the pair is not cached on failure, so
  it is retried.** Expected share: ≈ 2.2 % of A's J9 attempts ≈ 0.09 % of A's journeys (J9 weight 4 %); B's seed has no
  blocked accounts. Not a validity problem (threshold 1 %); A's J9 does a cheap 404 instead of a write in those attempts.
  Recorded, NOT fixed (rule 1); exact per-run counts come from `journeys-*.json` `endpoints.dm_open.failed`. Secondary A app
  inconsistency: the friends list shows accounts the DM path treats as invisible.
- 21:47:53 Phase 2 GATE RESULT: `thesis/results/rounds/20261024-2137-v3-gate/` — **2/2 valid** (smoke A + B), 0 restarts,
  0 × 429, write_check A 68/68 · 21/21 · 21/21, B 45/45 · 21/21 · 23/23; failed checks A 0.17 % (the 2 × D17 `dm_open` 404),
  B 0.00 %; core p95 22 vs 22 ms; J4 med 462 vs 1461 ms (B's ≥ 1-s upload poll, D12); J8 probes 8/8 both, receipt med
  1243 vs 6 ms (D14); k6 ≤ 47 % of one core. REPORT.md complete (h6 sections, stats block). Gate GREEN → proceed.
- 21:50:10 **Phase 3 MEASURED ROUND STARTED (detached)**: `setsid nohup make ab-round ARGS="--plan demo:10,capacity:1 --label v3"`
  → archive **`thesis/results/rounds/20261024-2150-v3/`** (22 runs: demo 10 + 10 ABBA, capacity 1 + 1), stdout
  `03-round/round-stdout.log`, pid in `status.json`. Expected end ≈ 02:40–03:00. Host idle (no tunnels, no port-forwards, no
  containers except minikube; load 0.4). If THIS SESSION DIES: the round keeps running (own session id); a new session must
  NOT start another round — run `make ab-status` (exit 10 RUNNING / 0 DONE / 20 FAILED / 30 STALLED); on FAILED/STALLED:
  `setsid nohup make ab-round ARGS="--resume thesis/results/rounds/20261024-2150-v3 --plan demo:10,capacity:1" > …/03-round/round-stdout-resume.log 2>&1 &`.
  Right after DONE: run the retention-bound exports FIRST (`scratchpad export-round-window.py`, Prometheus port-forward + Loki
  port-forward, window = round.json started_epoch → status.json updated_epoch) — Prometheus keeps only 6 h.
- 23:55 Early-warning wake (wait loop exits on the first invalid run): **demo/B/run-5 INVALID — pod restart `redis-75c79bffcf-dnqb2`
  during the run** (9/9 earlier runs valid). Diagnosis (kubectl events + Prometheus via the round's own :19091 forward, read-only):
  B's Redis liveness/readiness probes are `exec redis-cli ping` with `timeoutSeconds: 1` (limits 250m/128Mi, B's base manifest);
  restart counter 12→13 at 21:27 (during B's image build), **13→14 at 23:37 (during A's demo run-5, Redis idle at 0.02 cores)**,
  14→15 at 23:50 (during B's run-5). All three at peak NODE load (5–6.7 of 8 cores busy) — the exec probe (runc exec of
  redis-cli) exceeded 1 s, not Redis itself (CPU 0.02–0.05 cores, throttling ≤ 19 %). Same class as A's RabbitMQ exec-probe restart
  at 21:25 (10 s timeout). Recorded as a finding (B's 1-s exec probe timeouts are fragile under node load); the round marks such
  runs invalid by design → to be redone with `--resume` after the round. Run B-5 itself: 0 failed requests/checks, write checks
  exact, WS 0 errors.
- 23:56 **FINDING (major, harness h6 — load generator saturates the shared host):** k6 CPU in the demo runs = **max 4.8–5.7 cores,
  p90 ≈ 3.8–4.5 cores, median ≈ 1.5–1.8 cores** (computed from `samples-*.json` k6_cpu_ticks; v1 demo: max 0.44 / 0.74 of ONE core);
  node busy avg ≈ 4.8 cores (v1 2.97), peaks 6.7 of 8, host load average up to 14 (8 vCPU shared by k6 + the whole cluster).
  Probable cause (common to both sides, new since mix v2): J4 synthesizes a 256×256 RGBA PNG per journey in k6's JS VM
  (`makePng` inside the timed journey; gate: ≈ 430 ms wall per J4 beyond its HTTP time; GC on other cores) — ≈ 40 ms of extra
  k6 CPU per journey on average at 3 % J4. Effect: **closed-model latencies are contaminated by CPU contention on BOTH sides**
  (demo core p95 A 223–644 ms, B 142–367 ms vs v1 19 / 61 ms; journeys/s 79.5 vs v1 91.6) while **cgroup CPU per journey stays
  robust** (A app 5.17 ms/journey in run-1 vs v1 5.02). Capacity (H2) will be bounded by the host, not the SUT, at high steps.
  Per rule 1 the round is NOT altered; to be confirmed after the round with a k6 micro-benchmark of `makePng`; follow-up option
  for the owner: an existing harness knob, `--k6-env J4_IMAGE_PX=<n>` (smaller synthetic photo; a new labelled round — variants
  are out of scope tonight).
- 01:59 Early-warning wake: **demo/A/run-10 INVALID — pod restart `rabbitmq-0` during the run** (18/19 earlier runs valid apart
  from B-5). A's RabbitMQ liveness probe = `exec rabbitmq-diagnostics status` (timeout 15 s, period 30 s, threshold 3; limits
  500m/512Mi) failed under peak node load at 01:55 (an Erlang CLI start inside a CPU-limited, busy broker container); in the
  same window MinIO readiness timed out and both backend pods went unready (`/health/ready` gates RabbitMQ) → 0.87 % failed
  requests in that run (write checks still exact, WS probes 220/220). Same mechanism as B's Redis restart: exec-probed
  infrastructure pods fail their probes when k6 + the cluster saturate the 8 host cores (see the 23:56 finding). Both invalid
  demo runs (B-5, A-10) will be redone with `--resume` after the retention-bound exports. Capacity runs (A, B) follow now.
- 02:17 Early-warning wake: **capacity/A/run-1 INVALID — pod restarts `postgres-0` (02:10:39) and `rabbitmq-0` during the run.**
  Per-step (journeys + samples + resources.json): c01 25 j/s ok (p95 22 ms), c02 50 ok (35 ms), c03 100 → 99.6 achieved,
  p95 409 ms, **k6 avg 4.0 / max 5.3 cores vs A app 0.53 + DB 0.48 cores**; c04 150 → 22 achieved, p95 60 s, 14 % failed (node
  overload → exec probes of Postgres/RabbitMQ fail → restarts → backend unready); c05–c11 collapse (100 % failed from c07),
  117 865 dropped iterations, k6 RSS up to 3.7 GiB (host swap already 2.2 GiB). Report line: knee c04 @ 150 (core p95 > 500 ms),
  sustained 100 j/s (v1: sustained 300, knee 400). **H2 tonight measures the shared host saturated by the load generator, not
  the SUT** (confirms the 23:56 finding). Capacity B is running now and is left to finish (record). Plan after the round:
  exports first; then `--resume` for the two invalid DEMO runs only (`--plan demo:10`) — a capacity redo with the same harness
  would reproduce the collapse (deliberate deviation from "same plan", recorded); then a k6 micro-benchmark of `makePng` on the
  idle host to confirm the cause.
- 02:33:36 **Phase 3 MEASURED ROUND DONE: 18/22 valid** (invalid: demo B-5 Redis restart, demo A-10 RabbitMQ restart, capacity A
  Postgres + RabbitMQ restarts, capacity B — see its run.json). `make ab-status` → `03-round/ab-status-after-round.txt`.
- 02:35 Retention-bound exports (`scratchpad post-round-export.sh`): `03-round/prometheus-round-window.json` (window 21:50:17 →
  02:35:25, step 15 s, 102 PromQL queries — the 86 QUERIES.md expressions + per-pod CPU/memory/throttling/restarts for both
  namespaces, ingress, node CPU/memory/swap — 0 errors, 8 empty = series this cAdvisor does not export), `events-all-namespaces-after-round.txt`,
  `pods-after-round.txt`. The Loki part (full-window `| json` filters) stalled > 10 min in Loki's SingleBinary → stopped by pid at
  02:46; Loki keeps 24 h → re-run later with line filters before the parser, in hourly chunks, when no measured run is active.
- 02:46:47 **RESUME launched (detached)**: `setsid nohup make ab-round ARGS="--resume thesis/results/rounds/20261024-2150-v3 --plan demo:10"`
  (stdout `03-round/round-stdout-resume.log`): valid demo runs kept, demo B-5 and A-10 moved to `invalid-attempts/` and redone.
  Capacity deliberately NOT redone (same harness → same host collapse; recorded deviation from "same plan"). ETA ≈ 03:13.
- **NOTE: clocks changed CEST → CET at 03:00 CEST (= 02:00 CET) on 2026-10-25. Entries below are CET (UTC+1); JSON epochs
  are unambiguous.**
- 02:11 CET RESUME DONE: **demo 10 + 10 valid** (B-5 and A-10 redone valid; invalid attempts kept in `invalid-attempts/`);
  REPORT.md regenerated (plan field reads `demo:10` after the resume; capacity runs listed INVALID). Copied to `03-round/`:
  REPORT, round-summary, round.json, meta-A/B (+ `.resume-*.json`), status.json, stats.json, ab-status after round/resume.
  No `capacity-per-step-cpu.json` (written only for valid capacity runs).
- 02:13–02:14 CET `makePng` micro-benchmark on the idle host (`05-analysis/k6-makepng-bench/`): 256 px 603 ms / ≈ 0.7 core-s
  per image (10 images: wall 6.26 s, user 7.04 s incl. startup), 128 px 121 ms, 64 px 32 ms, 32 px 8 ms — root cause confirmed.
- 02:14–02:31 CET Loki export with line filters (`scratchpad export-loki.py` → `03-round/logs-round-window/`, summary.json):
  A warnings/errors 1 981 lines (one 1-h window timed out and was recovered in 2-min windows), A slow > 1 s 7 383, A 5xx 375,
  A 499 0, B WARN/ERROR 7 750, B slow > 1 s 23 012, B 5xx 0, B 499 0; Loki's total-line counts are inconsistent (request
  lines > all lines) → not used.
- 02:15–02:31 CET Phase 4.1 A functional on a fresh clone of `thesis-v3-a` (`01-functional/A/`, TRX ×8): build OK with
  **32 warnings = NU1903 SSH.NET 2024.1.0 (2 new HIGH advisories, transitive via Testcontainers, test projects only)**, tests
  **259 passed / 0 failed / 0 skipped** (48 integration on Testcontainers), `dotnet format --verify-no-changes` clean,
  frontend npm ci / typecheck / lint OK, vitest **71/71**, `next build` OK.
- 02:15 CET first B functional attempt + parallel A tests + pristine reseed → host swap-thrash (load 65, swap 3.95/4 GiB,
  iowait 60 %) → B attempt stopped by pgid at 02:21 (`01-functional/B-attempt1-aborted/`), relaunched 02:32 after A finished.
- 02:16–02:31 CET Phase 4.2 pristine reseed (A then B, counts exact) + seed shape (`sql-per-journey/pristine-extra/`) + SQL
  per journey (`log_statement=all` window, reset to `none` verified on both): re-classified offline with connection-aware
  rules (`sql-classify.py` → `summary-classified.{txt,json}`; the first-pass `summary.txt` misfiled multi-line exporter
  queries and River's sqlc-named polls).
- 02:35–02:45 CET B functional: `go test -race ./...`, Postgres lane, Redis lane green; **golangci-lint step failed on
  `proxy.golang.org: TLS handshake timeout` (environment, not a lint finding) → to be re-run.** During `go test -race` the host
  hit load ≈ 80 and the cluster's exec probes failed en masse: A RabbitMQ restarts 16 → 25, B Redis 15 → 33, B gomx pod and MinIO
  once each (01:41–01:44 UTC) — no measurement was running; the cluster must settle before Lighthouse/screenshots.
- 02:48–02:52 CET B functional finished: `go test -race` 388/0/25, Postgres lane 81/0/0, Redis lane 1/0/0, lint:ts clean;
  golangci-lint rerun alone → clean (rc 0). `01-functional/functional-summary.json` written (A PASS, B PASS).
- 02:52–03:00 CET Phase 4.3 static (`scratchpad static.sh` → `04-static/`): trivy fs A 0 / B 0 fixable HIGH/CRITICAL (A had 13 on
  2026-10-18), misconfig 5 / 5, image scans 0 / 0 / 0; cloc core A 53 914 / B 30 575; images 178.9 + 248.8 MB vs 41.2 MB; first-load
  gzip 211 893 vs 145 257 B (B binary moved to `/app/server` — extraction redone, binary not kept in the archive); dependency
  counts. During trivy the host load briefly hit ≈ 160 (kernel reclaim + crash-looping pods); RabbitMQ was in CrashLoopBackOff
  after the Phase 4 overload and recovered by itself at 03:02 CET (backend 2/2 Ready). `STATIC-SUMMARY.md`, `static-summary.json`.
- 03:03–03:10 CET Lighthouse on the cluster, quiet host (load 2–5): A/B × mobile/desktop (`04-static/lighthouse/`, comparison
  with 2026-10-18 in `comparison-v3-vs-20261018.md`): mobile feed perf A 0.72 / B 0.89 (TBT 698 / 11 ms), desktop 0.97 / 1.00.
  Figures re-rendered incl. `lighthouse-cluster.png` (scratch-patched script, N = 10 labels; `thesis/figures/v3/NOTES-v3.md`).
- 03:11 CET screenshots (`04-static/screenshots/`, 12 files incl. phone width): real content on both; A's home page renders
  again (v1 regression fixed in the v3 web image).
- 02:55–03:15 CET Phase 5 docs: `05-analysis/CAMPAIGN-REPORT.md` (§0–§8), `05-analysis/ARTICLE-RESULTS-DIGEST.md` (Tables 1–5,
  key findings, caveats, figures), `AB-COMPARISON-v1.md` §9b (verbatim REPORT tables) + D17 in §6.2 + status row,
  `AB-START-HERE.md` (header, findings rows, traps 27–31), `CLAUDE.md` current-state entry, memory note `ab-campaign-v3-2026-10-25`.
- 03:12 CET `index.json` (498 artifacts, every one described). Backup `~/backups/forum-dotnet-2026-10-25-v3/`: tar.gz of the
  campaign folder (34 MB), the round, the gate and `thesis/figures/v3` (incl. every gitignored file), the prompt copy,
  `SHA256SUMS`, `INVENTORY.txt` (tracked-vs-ignored per directory). Git would add ≈ 14.3 MB (7.3 MB of it =
  `03-round/prometheus-round-window.json`); gitignored, backup-only: `*.log`, `*.jsonl`, `*.patch`, `*.xml`, trivy `*.json`
  reports, Lighthouse `*.report.{html,json}`, `02-live-checks/raw/`, `logs-before-round/`, `logs-round-window/`.
- 03:15 CET **CAMPAIGN COMPLETE.** Repo state: tracked changes only in `CLAUDE.md`, `docs/architecture/AB-START-HERE.md`,
  `thesis/results/AB-COMPARISON-v1.md`; new untracked `thesis/results/campaign-20261024-2129/`, `thesis/results/rounds/20261024-2137-v3-gate/`,
  `thesis/results/rounds/20261024-2150-v3/`, `thesis/results/rounds/LATEST`, `thesis/figures/v3/`; NO code changes in either repo;
  gomx untouched (clean at `thesis-v3-b`). Cluster left RUNNING in bench shape (no HPA, A tracing off, A limiter restored by
  the round's trap; all pods Ready at 03:02 CET; kube-state-metrics/storage-provisioner were crash-looping after the Phase 4
  overload and recover on their own). Nothing committed or pushed by the operator.
