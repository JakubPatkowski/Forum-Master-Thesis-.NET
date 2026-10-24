# A/B comparison — START HERE

The single entry point to the thesis comparison of **Architecture A** (`forum-dotnet`, this repo) and
**Architecture B** (`gomx`, Hubert Ożarowski's repo at `../gomx`). Read this first; it tells you which document is
authoritative for what, what the current state is, how to re-verify everything from scratch and which traps cost
hours before.

| | |
|---|---|
| **Last updated** | 2026-10-24 — comparability fixes implemented on BOTH trees (`AB-STATUS-AND-PLAN-2026-10-23.md` §0b lists them; `AB-COMPARISON-v1.md` §5.8 is the measurement-relevant summary); previous: 2026-10-23 status snapshot, 2026-10-18 first full campaign |
| **State** | Both apps and the shared harness are **aligned (v1)** and **measured**: round `thesis/results/rounds/20261018-0051-v1/` (demo 5+5, stress 1+1, capacity 1+1 — 14/14 valid, 0 % failed, 0 × 429), functional gates green on both (A 248/0/0, B 378/0/25 + 81/0/0), live checks, static metrics (trivy/cloc/sizes/Lighthouse) — all archived with the exact patches + image digests under test. Results tables: `thesis/results/AB-COMPARISON-v1.md` §9; analysis, anomalies, threats and the v2 list: `thesis/results/campaign-20261017-2357/05-analysis/CAMPAIGN-REPORT.md`. |
| **Next** | **2026-10-24:** both working trees carry the comparability fixes (uncommitted — owner commits A on `31-feat-phase-13---ab-benchmark` and B on `feature/master-thesis`, tags `thesis-v2-a`/`thesis-v2-b`); the cluster is **stopped** → `minikube -p forum start`, rebuild both from the clean HEADs (`make mk-deploy ARGS=--seed-benchmark`, `make gomx-deploy ARGS=--reset-db`; the B overlay now sets `GOMX_CARD_PREVIEW_CHARS=300` and A's seed carries the social graph), verify bank + profile (`ab_check_profile` expects the new B env), smoke, then the **v2 round** (§8.1) plus the `--real-spa` round. Results chapter for the class on **2026-10-25** from round v1 (`thesis/figures/v1/`) or v2 |
| **Deadline context** | thesis-seminar class on **2026-10-25**: a results chapter (subsections, tables with units + in-text references, captioned figures) — skeleton in `AB-STATUS-AND-PLAN-2026-10-23.md` §7, data from round v1 (or v2 if it runs on 2026-10-23/24). Earlier: class 2026-10-18 — done |

---

## 1. Context in 60 seconds

- The master's thesis compares two complete implementations of the same forum at the **system level**: A = decoupled
  **React SPA (Next.js, CSR) + .NET 10 modular monolith JSON API**; B = **Go SSR monolith (templ + HTMX)**. Same
  feature core (spaces, threads, nested comments, reactions, search, profiles, friends/DMs, live updates).
- Hypotheses: **H1** resources per served user journey at equal load; **H2** throughput ceiling (highest offered
  journey rate with journey p95 ≤ 500 ms, failures ≤ 1 %); **H3** dependency surface / CVEs / code size / dev-loop.
- Paradigm differences (HTML vs JSON, sqlc vs EF Core, int64 vs ULID, ILIKE vs tsvector, Redis vs RabbitMQ+outbox,
  cookie sessions vs JWT, server ingest vs presigned uploads) are **the subject of study and are kept**. Everything
  else is equalized, or recorded as a deviation when equalizing would mean rewriting a feature.
- Both run in ONE minikube cluster (profile `forum`): A in namespace `forum-dotnet`, B in `gomx`, one shared
  ingress-nginx, one monitoring stack (A's). **One k6 harness** in this repo (`load/k6/`) drives both through
  per-target adapters. Runs are sequential (the idle side's app is scaled to 0).
- Repos and branches: A = `~/projects/forum-dotnet`, branch `31-feat-phase-13---ab-benchmark` (PR #30; HEAD
  `57f63d0` = the 2026-10-17 alignment, + the uncommitted 2026-10-18 re-verification edits); B = `~/projects/gomx`,
  branch `feature/master-thesis` (Hubert's B-01…B-14 at `e13f18f` + the pushed alignment commits `4b43557`, `f03d46a`
  + the uncommitted 2026-10-18 edit). **Claude never commits or pushes** — the owner does.

## 2. Document map

| Document | Role | Status |
|---|---|---|
| **this file** | entry point, checklist, traps | current |
| `docs/architecture/AB-STATUS-AND-PLAN-2026-10-23.md` | **2026-10-23 snapshot**: state of both repos/cluster/archives, stack + core inventory from code, reviewer objections and how each closes, test layers, gaps, unification decisions, dated plan with commands, results-chapter skeleton, thesis/article outline | current |
| `thesis/results/AB-COMPARISON-v1.md` | the v1 comparison: systems, hypotheses, environment, dataset, journeys, fairness invariants F1–F8, deviation register, runbook, results §9 | current — **results to fill** |
| `docs/architecture/AB-ALIGNMENT-INSTRUCTIONS-A.md` / `-B.md` | work items A-01…A-18 / B-01…B-15 with code; §4 = the wire **interface contract** (identical in both); §6 deviations; §7 open decisions | current (status boxes + §4 updated 2026-10-17) |
| `docs/architecture/AB-SCOPE-ALIGNMENT-2026-10.md` | decision record (what was decided, why, implementation status) | current |
| `docs/architecture/AB-VERIFICATION-FABLE5-PROMPT.md` | the prompt for an independent re-verification session | current |
| `docs/specs/forum-spec.md` | shared functional specification | **DRAFT** — Hubert's sign-off pending; its §9 load protocol is superseded by harness v1 (callout in §9) |
| `thesis/results/DAY0-2026-10-17.md` | first preliminary numbers (unaligned B, v0 harness) | historical — read its **erratum §0** |
| `thesis/results/rounds/<stamp>-<label>/REPORT.md` | generated per round (`scripts/ab_report.py`) | generated — never edit by hand |
| `load/k6/targets/README.md` | harness adapter interface + wire rules | current |
| `../gomx/deploy/k8s/bench/README.md` | B's bench overlay | current |
| `AB-CURRENT-STATE-AUDIT.md`, `AB-THESIS-GAP-ANALYSIS.md`, `AB-UNIFICATION-MASTER-PLAN.md`, `AB-UNIFICATION-PLAN-ARCHITECTURE-{A,B}.md`, `PROPOZYCJA-UJEDNOLICENIA-A-B.md`, `POST-9C-ROADMAP.md` | July 2026 audit, gap analysis and plans | background (partly superseded by the October documents) |

## 3. Glossary of identifiers

| ID | Meaning |
|---|---|
| H1, H2, H3 | hypotheses (§1) |
| J1 … J9, nav | journeys: J1 space feed, J2 thread open, J3 comment, J4 thread create (+image), J5 reaction toggle, J6 search, J7 profile, J8 WebSocket hold, J9 DM, nav = list spaces |
| F1 … F8 | fairness invariants (`AB-COMPARISON-v1.md` §6.1) |
| D1 … D7 | deviations decided by Jakub on 2026-10-17 (kept + documented); D-1 … D-18 = the older register in the instruction docs §6 |
| A-xx / B-xx | work items for A / B (instruction docs §3) |
| `mix v1`, harness `h3-2026-10-17` | journey weights / adapter behaviour version recorded in every result |
| demo / stress / capacity / warmup / smoke | k6 profiles (closed model: demo, stress, smoke, warmup; open model: capacity) |

## 4. Current state (2026-10-18 night)

### 4.1 Aligned and verified
Common profile on both (2 fixed replicas, no HPA, 250m/256Mi → 1/512Mi, probe timeouts 5 s, tracing off, limiters
neutralized + recorded, pool 30, **identical Postgres 18.4 Debian** with the same args/resources), TLS on both hosts,
gzip at the shared ingress for both, page sizes 20/20, B's per-friend fan-out and OAuth mock off, deterministic
Benchmark seeds with matching volumes **and** shape (§5 V-6 numbers), one harness with layout-independent selection
rules, scenario-only measurement windows, reseed + fresh pods per run, ABBA order, generated report with 95 % CIs.

### 4.2 Findings log (what was wrong and how it was fixed)

| When | Finding | Fix |
|---|---|---|
| Day-0 (2026-10-17 night) | B unmodified (page 8/10, fan-out, gzip, pool 8); journeys/s and CPU included the k6 setup; k6 sent no `Accept-Encoding` (nothing compressed anywhere) | v1 harness; Day-0 erratum |
| review | Hubert's B-05a missing: `pool_max_conns` in `DATABASE_URL` **crashes B at startup** (goose opens the raw DSN via `database/sql`) | `postgres.go applyPoolEnv` + `GOMX_DB_MAX_CONNS=30`; secret patch removed |
| review | B adapter's search words came from sentences B-09 removed → ~0 hits on B | one shared word list |
| review | A's search (and feed/detail/tree/profile lists) returned private-category content to anyone (security + 48 % more J6 rows than B) | **A-12** read gate + integration test |
| review | B placed posts round-robin: 533 private posts vs A's 266, smaller public set | B uses A's placement rule (50 % into the 3 largest spaces) |
| review | thread pools differed (A newest 2×50, B newest 2×20) → B opened threads with ~35 % more comments | whole-corpus pools on both |
| review | ~half of A's J5 calls were idempotent no-ops (no write); every B vote writes | per-VU like state → always a real toggle |
| review | B Postgres `18-alpine` (musl) vs A `postgres:18` (Debian) | B overlay uses `postgres:18` (uid 999) |
| implementation | MinIO withdrew its public images → the integration test factory silently **skipped the whole integration suite** | test image pinned to the cluster's release (cached locally) |
| implementation | 5 GB of `frontend/src-tauri/target` sent as docker build context per image | `.dockerignore` files |
| implementation | same `-dirty` image tag → no rollout, old pods keep running | `deploy.sh` forces a restart for dirty tags |
| implementation | pod-level `runAsGroup` without `runAsUser` → B's Postgres pod never starts on the docker runtime | overlay sets the uid/gid/fsGroup trio |
| dry run | read-only warm-up left A's write paths JIT-cold (J3/J5 p99 150–175 ms, 16.7 vs 10.6 CPU-ms/journey) | warm-up with ~6 % writes |
| dry run | B lists spaces by name → "first three = hot" were B's three smallest | hot = three largest by crawled size |
| dry run | the heaviest thread (~230 comments) is in a small space on A, the largest on B → a page-1 head rule opened it ~3× more on B | thread journeys draw uniformly from the whole public corpus |
| **re-verification 2026-10-18** | B ran a `GetMembership` query for **anonymous** viewers — once per `/c/{slug}` view (J1) and **once per community on `/communities`** (`nav` = 1 list + 12 pointless PK lookups; A's `nav` = 1 query). Found with `pg_stat_statements` on a local copy | `handlers_community.go`: skip the lookup when `uid == 0` (+ `TestAnonymousBrowseSkipsMembershipLookups`, `newTestServerWithStore`) |
| re-verification | A had **no owner index** on `threads`/`comments`: every J7 profile read seq-scanned both tables (`user_stats_v` 4.1 ms, user threads 1.2 ms) while B has `idx_posts_author` / `idx_comments_author` (0.65 ms) — "an accidental seq scan on one side" | Content migration `AddOwnerActivityIndexes` (`ix_threads_owner_activity`, `ix_comments_owner_activity`, partial `is_deleted = false`) → 0.9 / 0.25 ms |
| re-verification | B's reseed left the pod that ran the in-process seeder in the measured pair; A's pods were restarted after its seed Job — "fresh pods" meant two different things | `ab_reseed b` scales to 0 after the seed, then back to `AB_REPLICAS` |
| re-verification | H1 "CPU per journey" mixed each architecture's **standing cost** (A: RabbitMQ ≈ 0.1 core idle + 5 relay loops; B: River polling 1/s) with the per-journey cost — at smoke load A's "bus CPU per journey" was 10 ms of idle RabbitMQ | every run also reads an idle window 20–50 s after k6 exits (`ab_window.py --idle-*`) → `resources.json.idle`; REPORT rows "idle cores" + "MARGINAL CPU per journey" |
| **campaign 2026-10-18** | the anonymous SPA home page `/` failed with "Category not found" on any dataset with a private category: the home feed merged all 12 categories from `GET /api/content/categories` and the A-12 read gate's 404 rejected the whole `Promise.all` (regression introduced by A-12; not on the harness path) | `feed-merge.ts` `exhaustSource()` + `use-home-feed.ts` treats 404/403 per source as "contributes nothing" + `feed-merge.exhaust.test.ts`; verified on a local prod build (`campaign-…/05-analysis/A-home-after-fix-local.png`); deployed web image still old until the next `make mk-deploy` |
| campaign | B's WebSocket holds break under load: stress 308 subscribed / 148 errors, capacity 222 / 152 (A 0 / 0) — the hub closes slow consumers by design (`hub.go` "client too slow to keep up") while broadcasting every post/comment/vote fragment to every socket | recorded (CAMPAIGN-REPORT §6.2); v2: measure J8 receipt latency/loss; Hubert to review the slow-consumer policy |
| campaign | B logs `500` for client-cancelled requests (8 × "canceling statement due to user request" when an orphaned k6 died) — outside every measured window | recorded; suggest 499 |
| campaign | H2: the capacity knee is the **1-CPU Postgres** on both sides (B's DB at 1.00 core from 300 offered j/s, A's at 0.93 from 400) — B hits it at half the throughput because its DB cost per journey is 1.7–1.9× A's (J6 under D4, member_count COUNT, SumPostVotes) | recorded (`03-round/capacity-per-step-cpu.json`); v2: selective word bank, N ≥ 3 capacity repeats |
| campaign | the first v1 round attempt died with the operator session (Bash-tool background tasks are killed on session exit) | relaunch detached (`setsid nohup make ab-round …`); archive `20261018-0029-v1-ABORTED-session-crash` excluded |
| **2026-10-24** | B pushed every comment to every socket: `broadcastComment` lacked the community-visibility gate `broadcastPost` has → private-community comment HTML reached outsiders and anonymous sockets | gate + member-only delivery (`commentAudience`) + `TestPrivateCommunityCommentsReachMembersOnly` |
| 2026-10-24 | D2: B cards rendered whole bodies, A cards none → unequal card work and bytes per J1/J6 | **both sides ship a 300-char plain-text excerpt** (A `thread_excerpt()` after the LIMIT; B `GOMX_CARD_PREVIEW_CHARS=300` in the bench overlay, product default unchanged); `ab_check_profile` expects the new B env |
| 2026-10-24 | D6: A's Benchmark seed had 0 social rows, B 2 400 + 80 friendships / 2 000 DMs | A seeds the mirrored graph (`BenchSocialGraph`; e2e `SeedFlowBenchmarkTests`) |
| 2026-10-24 | D10: `has_permission` recomputed the mask on every write with a non-sargable predicate | one-pass sargable `effective_mask` + cache fast path (migration `OptimizeEffectiveMask`; 0 mismatches over 3 300 pairs; −14 % live, 5.5× cached) |
| 2026-10-24 | A: concurrent `POST /threads` sharing a brand-new tag slug → 500 (unique violation; found by the h5 smoke on the Development seed; Benchmark pre-creates every harness slug) | `INSERT … ON CONFLICT (slug) DO NOTHING` get-or-create + e2e regression test |
| 2026-10-24 | B: hub queue 32 closed ~150 holders in stress/capacity; cancelled requests logged as 500 | queue 256 (close-on-full kept); 499 in `renderStatus` + `RequestLogger` + test |
| 2026-10-24 | D1 had no measurement | `REAL_SPA=1` / `ab-round.sh --real-spa` replays the derived cold-cache waterfall (`load/k6/targets/README.md`); harness **h5-2026-10-24** |
| 2026-10-24 | A mobile: signed-in TopNav 532 px on a 412 px phone, tap targets < 40 px; 13 fixable frontend CVEs | CSS fixes (Playwright 24/0/8, `test.fail` markers removed); next 15.5.28 + overrides (sharp/nanoid/postcss/source-map-js) |

### 4.3 What remains different (documented, not leveled)
D1 (default adapter; `--real-spa` variant since 2026-10-24) A's adapter is an idealized SPA client (fewer calls than the real SPA, no client cache) → A's server cost is a
lower bound — **quantified 2026-10-18** from the SPA code: a cold space page = feed + batch + slug→id + 20 thread-icon
lookups + ≤ 20 avatar lookups + 1 category icon (≈ 43 small calls, each one attachment/PK query) vs the adapter's 2; a
cold thread page = detail + tree + batch + author stats + attachments + related search + icons + one avatar per
distinct author (≈ 8 + N) vs the adapter's 3; B's real browser = 1 HTML + 1 Web-Vitals beacon per page (+ immutable
assets from cache, one WebSocket per tab) vs the adapter's 1 · D2 **closed 2026-10-24** (300-char excerpt on both; B product default still full bodies) · D3 reads are anonymous (favours
B) · D4 the 31-word bank makes J6 match ~every thread on both · D5 seed timestamps differ (neutralized for thread
journeys) · D6 **closed 2026-10-24** for social rows (tags/soft-deleted/blocked remain A-only) · D7 write journeys measure the
write only · D8 runtime memory policy = platform defaults (A: .NET **Server GC**, `System.GC.Server: true`; B: Go
GOGC=100, no GOMEMLIMIT) — part of each platform's default posture, stated next to every memory number ·
**D9** (2026-10-18) A's per-request stack overhead: every pooled connection use ends with Npgsql's 7-statement reset
(`CLOSE ALL; DISCARD TEMP; …`, no extra round trip) and the JSON API pays the A-12 read gate per endpoint (J2 = 5
statements over 3 requests vs B's 3 over 1) — paradigm, measured, stated · **D10** A resolves every write's
permission live in SQL (`has_permission` → `effective_mask`, ≈ 1.9 ms; the `effective_perm_cache` table is maintained
but not read on that path) while B's comment/vote path only checks community visibility (no query for a public
space) — A's locked assumption 1 (RBAC + ACL in SQL), measured; the cache read is a §8 item · **D11** (= D2 with
numbers) B's feed/search cards carry the **whole rendered body**: J1 page 174 KB HTML / 16 KB gzip vs A's 8 KB / 1.5 KB
JSON + 1.5 KB / 0.5 KB batch; J6 174 / 15.7 KB vs 8.0 / 1.8 KB.

### 4.4 Open items and risks
- **2026-10-23:** three gomx files from the 2026-10-18 session are still uncommitted (the N+1 regression test, the
  `newTestServerWithStore` helper, the bench README D4 note) although `5c569f7` is pushed; the cluster is stopped and
  its images are `-dirty`-tagged builds of the (now committed) trees; the A branch was slimmed (raw result files are
  gitignored — keep `thesis/results/` on disk and `~/backups/forum-dotnet-2026-10-23/`). **B leak confirmed in code:**
  `app/forum/web/live.go` `broadcastComment` lacks the community-visibility gate that `broadcastPost` has → private-
  community comment HTML reaches every socket (anonymous included). Full list + plan: `AB-STATUS-AND-PLAN-2026-10-23.md`.
- The deployed `forum-dotnet-web:git-57f63d0-dirty` image carries the anonymous home-page regression (fixed in the working tree
  2026-10-18 03:47, not yet built/deployed); the API image is unaffected. `make mk-deploy` rebuilds both.
- A's frontend toolchain has 13 fixable HIGH/CRITICAL CVEs (trivy 2026-10-18: next@15.5.20 ×5, sharp ×3, nanoid ×2, postcss ×2,
  source-map-js ×1); B 0. Upgrade before the next static snapshot.
- **MinIO images can no longer be pulled** (Docker Hub repo gone, quay.io unauthorized). Backup of the cluster's
  images: `~/.cache/forum-dotnet/images/minio-images-2025-09.tar` (minio + mc). Restore:
  `minikube -p forum image load ~/.cache/forum-dotnet/images/minio-images-2025-09.tar` (cluster) /
  `docker load -i …` + `docker tag minio/minio:latest minio/minio:RELEASE.2025-09-07T16-13-09Z` (local tests).
  **Never `make mk-delete` / recreate the cluster without that file.**
- GitHub CI cannot pull the pinned MinIO image either → the integration job will skip silently until a mirror is set.
- Hubert has not yet reviewed the alignment edits on his branch; `forum-spec.md` is unsigned.
- Not yet measured: J4 (+upload), J9, J8 receipt latency, HPA classes, logged-in browsing, client-side metrics on the
  benchmark data. See §8.
- Hubert's GitLab pipeline reportedly fails on `f03d46a`. The fast lane (`lint:go` = check-generated + templ fmt +
  gofmt + vet + golangci-lint 1.64.5; `test:go` = `go test -race ./...`) and the MR-lane `test:go:postgres` were
  replayed on 2026-10-18 in the CI image (`golang:1.26-alpine`) at `f03d46a` **and** with the new edit: all green.
  Candidates that cannot be replayed here: `lint:ts` (bun), the runner's Go toolchain download (`go 1.26.1` in
  `go.mod`), `e2e:playwright`/`verify:container` (MR lane only). Read the failing job's log; nothing in the seed /
  overlay / handler edits touches generated code, templ files, TypeScript or the demo seed the e2e suite uses.
- The cluster's pod processes are visible in the host's `ps` (minikube's docker driver shares the kernel): two
  `dotnet Forum.Api.dll` as uid 1654 ARE the backend replicas — never `pkill` by name. Stop local API/compose stacks
  before a measured round.

## 5. Re-verification checklist (from scratch; every step has an expected result)

Helper for the lib-bench functions (run from A's repo root): `abx() { bash -c "source scripts/lib.sh; load_env; source scripts/lib-bench.sh; $*"; }`

| # | Check | How | Expected |
|---|---|---|---|
| V-0 | Baseline | `git -C ~/projects/forum-dotnet status -sb; git -C ~/projects/gomx status -sb; git -C … log -3 --oneline`; `minikube -p forum status`; `kubectl get pods -A` | know exactly which commits are under test; all pods Running |
| V-1 | A builds + tests | `cd backend && dotnet build Forum.slnx && dotnet test Forum.slnx && dotnet format Forum.slnx --verify-no-changes` | all pass, **0 skipped** (a skip = Testcontainers could not start, e.g. missing MinIO image) |
| V-2 | B builds + tests (no Go on the host) | `bash ../gomx/scripts/ensure-templbook-embed.sh`; postgres:18 container on a docker network; `docker run --rm --network <net> --user $(id -u):$(id -g) -e HOME=/tmp -e GOCACHE=/gocache/build -e GOMODCACHE=/gocache/mod -e GOFLAGS=-buildvcs=false -e DATABASE_URL=postgres://gomx:gomx@<pg>:5432/gomx?sslmode=disable -v <cache>:/gocache -v ~/projects/gomx:/src -w /src golang:1.26-alpine sh -c 'go vet ./... && go test ./app/... ./internal/...'`; same image: `gofmt -l app internal cmd`, `go run github.com/golangci/golangci-lint/cmd/golangci-lint@v1.64.5 run` | green; gofmt lists nothing |
| V-3 | B overlay | `kubectl kustomize ../gomx/deploy/k8s/bench` | no HPA; `GOMX_DB_MAX_CONNS=30`; no `pool_max_conns`; `postgres:18` (not alpine); pod-level runAsUser/runAsGroup/fsGroup 999 |
| V-4 | Common profile live | `abx 'ab_apply_profile a; ab_check_profile a; ab_check_profile b; ab_restore_limiter_a'` | every line `ok` on both sides |
| V-5 | Same Postgres | `kubectl -n forum-dotnet exec postgres-0 -- psql -U forum -d postgres -tAc 'select version()'`; same in `gomx` with `-U gomx -d gomx` | identical version string (18.4 Debian) |
| V-6 | Seed shape | reseed both (`abx 'ab_reseed a; ab_reseed b'`), then the SQL in §5.1 | A vs B: users 800/800; threads 1600/1600; private threads 266/263; public live 1317/1337; pinned 16/16; avg body 1914/1922 chars; comments 9000/9000; comments per public thread mean 5.73/5.67, median 4 both, max 234/225; reactions 11250+3750 on both, 0 downvotes; J6 hits for `kubernetes` 1313/1337 |
| V-7 | Wire contract | read `targets/a.js`, `targets/b.js` side by side with A's endpoints and B's handlers (`app/forum/web/handlers_*.go`); instruction docs §4 | every journey does the same user-visible work on both sides or the difference is in the deviation register |
| V-8 | Harness invariants | `load/k6/main.js`, `targets/*.js`, `lib/wire.js` | shared `SEARCH_WORDS`; whole-corpus pools (setup logs `1317` / `1337 threads pooled`); hot spaces = 3 largest; thread journeys uniform over the corpus; A J5 toggles state; `Accept-Encoding: gzip`; unparsed bodies discarded; window excludes setup; summaries contain no tokens |
| V-9 | Compression | `curl -sk --resolve forum.local:443:$(minikube -p forum ip) -H 'Accept-Encoding: gzip' -D - -o /dev/null 'https://forum.local/api/content/search?q=kubernetes&limit=20'` and the same for `https://gomx.local/search?q=kubernetes` | `content-encoding: gzip` on both (from the ingress; B's pods do not compress) |
| V-10 | Security posture | `curl -sk --resolve gomx.local:443:… -D - -o /dev/null https://gomx.local/auth/google`; A: anonymous search never returns private-category threads; a private thread id → 404 | B: 303 to `/login?error=oauth_unavailable`, no `forum_session`; A: 404 / omitted |
| V-11 | Smoke gate | `TARGET=a bash scripts/run-load-test.sh smoke` and `TARGET=b bash …` (A's limiter raised first: `abx 'ab_apply_profile a'`) | `all thresholds passed`, `failed=0.00%`, seven journey rows |
| V-12 | Pipeline | `make ab-round ARGS="--plan smoke:1 --label verify"` | both runs `valid=True`, `resources.json` + REPORT.md generated |
| V-13 | Adversarial | hunt for asymmetries nobody listed yet (§5.2) | every finding fixed or added to the deviation register |

### 5.0 Results of the 2026-10-18 re-verification (independent session)
- V-0 A `31-feat-phase-13---ab-benchmark` @ `57f63d0` clean, B `feature/master-thesis` @ `f03d46a` clean; cluster up
  (all pods Running, restarts from a host reboot ~2 h earlier); deployed images were `git-ee0faee-dirty` /
  `gomx:bench-e13f18f-dirty` → both rebuilt from the committed trees + the new edits before the gates:
  `forum-dotnet-api:git-57f63d0-dirty` (+ `-web`), `gomx:bench` = `gomx:bench-f03d46a-dirty` (sha256 `80140d5f…`).
  Pristine reseed in-cluster: A 1 317 public threads / 5.73 / 4 / 234, thread-id md5 identical to the pre-reseed
  database (determinism); B 1 337 / 5.67 / 4 / 225; both 800/1600/9000/15000.
- V-1 build 0 warnings · **248 tests, 0 failed, 0 skipped** (46 integration) · format clean — twice (before and after
  the owner-index migration).
- V-2 replayed in `golang:1.26-alpine`: lint:go all green, `go test -race ./...` all ok, Postgres-lane adapter tests ok
  — at `f03d46a` and with the anonymous-membership fix.
- V-3 overlay renders: 0 HPA, `GOMX_DB_MAX_CONNS=30`, no `pool_max_conns`, `docker.io/library/postgres:18`, pod-level
  999/999/999 (it renders `replicas: 2`; `gomx-bench-deploy.sh` pins 1 for the seeding boot).
- V-5 `PostgreSQL 18.4 (Debian 18.4-1.pgdg13+1)` on both. V-9 `content-encoding: gzip` on both (search A 8 016 →
  1 804 B, B 173 598 → 15 696 B; HTTP/2 to the ingress). V-10 B `/auth/google` → 303 `/login?error=oauth_unavailable`,
  no `forum_session`; A private thread → 404, 0 private rows in 50 search results.
- V-6 (before the gate reseed, DBs carried the dry run's writes): A 315/341/332 | 77/75/59/58/60 | private
  71/65/67/63 (266), pinned 16, public live 1 317, comments/public thread mean 5.85 (9 165 comments incl. ~165
  from the dry run), median 4, max 235, avg body 1 914, J6 hits 1 313; B 330/339/332 | 65/68/68/67/68 | private
  65/65/66/67 (263), pinned 16, 1 337, mean 5.80 (9 176), median 4, max 225, avg body 1 922, 11 395 + 3 750 votes,
  0 downvotes, J6 hits 1 337. Determinism: both seeds are index-driven; a reseed restores exactly 800/1600/9000/15000
  (checked by `ab_reseed`).
- V-7/V-8 harness and adapters read side by side with the handlers; per-request work counted with
  `pg_stat_statements` on local copies (`thesis/results/AB-COMPARISON-v1.md` §5.6) → the two fixes above.
- V-11 smokes through ingress + TLS: A 1 221 reqs, p95 11 ms, 0 % failed, 1 317 threads pooled; B 711 reqs, p95
  38 ms, 0 % failed, 1 337 pooled — `all thresholds passed` on both (invoke the runner through `bash`: the scripts are
  mode 100644 in git).
- V-12 `make ab-round ARGS="--plan smoke:1 --label verify"` → `thesis/results/rounds/20261017-2332-verify/`: reseed +
  fresh pods on both (B now restarts the seeder pod), profile verified, both runs `valid=True`, `resources.json`
  with the new `idle` section, REPORT.md with "idle cores" + "MARGINAL CPU per journey" rows; the limiter restore ran.
- V-13 adversarial: the four findings in §4.2 (2026-10-18 rows) + D9–D11 in §4.3; nothing else measurable found —
  B's `member_count` COUNT per page and `SumPostVotes` per vote, A's live ACL resolution and Npgsql reset are recorded
  as costs of each side's own design (§8 lists the optional follow-ups).

### 5.1 Seed-shape SQL
A (`kubectl -n forum-dotnet exec postgres-0 -- psql -U forum -d forum_net`):
```sql
SELECT c.slug, c.visibility, count(t.*) FILTER (WHERE NOT t.is_deleted) AS live, sum(t.is_pinned::int) AS pinned
FROM forum_content.categories c LEFT JOIN forum_content.threads t ON t.category_id = c.id GROUP BY 1, 2 ORDER BY 1;
WITH x AS (SELECT t.id, (SELECT count(*) FROM forum_content.comments cm WHERE cm.thread_id = t.id) n
           FROM forum_content.threads t JOIN forum_content.categories c ON c.id = t.category_id
           WHERE NOT t.is_deleted AND c.visibility = 'public')
SELECT count(*), round(avg(n), 2), percentile_cont(0.5) WITHIN GROUP (ORDER BY n), max(n) FROM x;
SELECT round(avg(length(body))) FROM forum_content.threads;
SELECT count(*) FROM forum_content.threads t JOIN forum_content.categories c ON c.id = t.category_id
WHERE NOT t.is_deleted AND c.visibility = 'public' AND t.search_tsv @@ websearch_to_tsquery('simple', 'kubernetes');
```
B (`kubectl -n gomx exec postgres-0 -- psql -U gomx -d gomx`):
```sql
SELECT c.slug, c.visibility, count(p.*), sum(p.pinned::int) FROM communities c LEFT JOIN posts p ON p.community_id = c.id GROUP BY c.id, 1, 2 ORDER BY c.id;
WITH x AS (SELECT p.id, (SELECT count(*) FROM comments cm WHERE cm.post_id = p.id) n
           FROM posts p JOIN communities c ON c.id = p.community_id WHERE c.visibility = 'public')
SELECT count(*), round(avg(n), 2), percentile_cont(0.5) WITHIN GROUP (ORDER BY n), max(n) FROM x;
SELECT round(avg(length(body))) FROM posts;
SELECT (SELECT count(*) FROM post_votes), (SELECT count(*) FROM comment_likes), (SELECT count(*) FROM post_votes WHERE dir < 0);
SELECT count(*) FROM posts p JOIN communities c ON c.id = p.community_id
WHERE c.visibility = 'public' AND (p.title ILIKE '%kubernetes%' OR p.body ILIKE '%kubernetes%' OR p.category ILIKE '%kubernetes%');
```

### 5.2 Adversarial review — places nobody has fully audited yet
- **Per-request work behind each journey**: count the SQL statements one J1/J2/J6/J7 request executes on each side
  (Postgres `log_min_duration_statement=0` on a scratch DB, or `pg_stat_statements`) — e.g. does B's anonymous page
  render run shell/sidebar/notification queries, does A run permission checks on anonymous reads?
- **Query plans**: `EXPLAIN (ANALYZE, BUFFERS)` for the feed, thread, comment tree and search queries on both — are the
  hot paths indexed equally well (an accidental seq scan on one side is a bug, not a paradigm)?
- **Background work** inside the measured pods: A's outbox relays / consumers / orphan sweep / realtime feed; B's River
  workers and pub/sub — polling intervals and their idle CPU.
- **Response bodies**: what each journey actually returns (sizes per journey in the report — KiB received per
  journey) and whether any extra content (e.g. D2 previews) is unbalanced beyond what is documented.
- **CPU throttling** under the 1-CPU limit (`container_cpu_cfs_throttled_periods_total`) — explains latency tails.
- **Runtime policies** (D8): GC/heap settings, thread pools (`DOTNET_ThreadPool_MinThreads=64` on A), GOMAXPROCS.
- **Load generator**: k6 CPU/RSS in every report — a saturated k6 invalidates the capacity knee.
- **Statistics**: CI overlap is a conservative heuristic; for the thesis tables consider Welch's t-test or
  Mann-Whitney U on the per-repeat values, and N ≥ 10 for hypothesis-tested tables (forum-spec §9.1).

## 6. Operating the benchmark

```bash
cd ~/projects/forum-dotnet
make mk-up                                    # start/reuse the cluster (calico, ingress + gzip, metrics-server)
make mon-up && make mon-check                 # monitoring (idempotent)
make mk-deploy ARGS=--seed-benchmark          # A: build + deploy (+ Benchmark seed)
make gomx-deploy ARGS=--reset-db              # B: build from ../gomx, apply its bench overlay, reseed (use --fresh only to recreate namespace + volumes)
make ab-round ARGS="--plan demo:5,stress:1,capacity:1 --label v1"   # ≈ 3 h unattended → thesis/results/rounds/<stamp>-v1/
make ab-report ARGS=thesis/results/rounds/<dir>                       # re-render a report
make mk-deploy                                # afterwards: restores A's production posture (HPA, limiter from configmap)
```
Keep `make tunnels` and Grafana browsing off during a round. Re-run only the failed profile with
`--plan <profile>:<n>` if the report lists invalid runs.

## 7. Traps that already cost hours

1. A `-dirty` image tag is reused by every rebuild of the same HEAD → `kubectl apply` sees no change (deploy.sh now
   restarts on dirty tags; for B the reseed path restarts pods).
2. `kubectl delete namespace` does NOT wipe minikube hostpath data → `make mk-down` (A) / `gomx-bench-deploy.sh --fresh` (B).
3. MinIO images are not pullable any more → keep `~/.cache/forum-dotnet/images/minio-images-2025-09.tar`.
4. k6 sends no `Accept-Encoding`; k6 `jar: null` does not disable the per-VU cookie jar (B adapter uses a fresh jar per request).
5. `pool_max_conns` in gomx's `DATABASE_URL` crashes startup; use `GOMX_DB_MAX_CONNS`.
6. Pod-level `runAsGroup` without `runAsUser` → "failed to make sandbox" on the docker runtime.
7. Completed migrate/seed Job pods carry `app=backend` → they pollute selectors/HPA (swept automatically by lib-bench).
8. A's access token lives 15 min → k6 setup + scenario must stay below that (all profiles do).
9. The integration test factory catches every startup exception → "Skipped" means broken infrastructure.
10. B lists spaces by name; never derive "hot" spaces from list order.
11. No Go toolchain on the host → docker recipe (V-2); B also needs `scripts/ensure-templbook-embed.sh` once.
12. kustomize: no trailing comments after flow mappings in strategic-merge patches.
13. A's runtime image is distroless (no shell in the pod) — inspect config from the repo/image, not via `kubectl exec sh`.
14. After a namespace re-creation, `rabbitmq-credentials` may be regenerated while the broker PVC keeps the old user →
    `kubectl -n forum-dotnet exec rabbitmq-0 -- rabbitmqctl change_password forum <new>`.
15. cAdvisor exports no pod network series on this cluster → transfer per journey comes from k6 `data_received`.
16. `kubectl top` lags 15–60 s; Prometheus window metrics are the primary resource source.
17. Count statements, do not guess: `pg_stat_statements` (`track=all`) on a local copy showed B's anonymous `nav` ran
    13 queries and A's profile seq-scanned two tables — both invisible in the latency tables at smoke load.
18. Any B handler that calls `Membership()`/`GetMembership()` must guard `uid == 0`; any new A list query by
    `owner_id` must have an index (profile paths were the only ones without).
19. `pkill -f Forum.Api` on the host also matches the cluster's backend pod processes (same kernel, uid 1654) —
    kill local processes by pid.
20. A's `has_permission` recomputes the mask live (≈ 2 ms per write); the cache table exists but is not read on
    that path (D10).
21. A Claude Code / Bash-tool background task dies with the session — start a measured round with `setsid nohup … &`
    (own session id) and resume by reading the archive's `round.log`; never start a second round while `pgrep -f ab-round.sh` lives.
22. Never kill by a pattern that your own command line contains (the shell kills itself, exit 144) and never by `next`/`dotnet`
    patterns at all: the cluster's frontend pod is `next-server` as uid 1000 and the backend pods are `dotnet Forum.Api.dll` as
    uid 1654 in the host's `ps`. Kill local processes by pid files (`setsid bash -c 'echo $$ > pidfile; exec …'`).
23. `docker compose start` also starts the stopped compose `api` container (an old image) on :8080 — `docker compose stop api`
    before running the working-tree API locally (Lighthouse A, dev loops).
24. `psql -c "ALTER SYSTEM …; SELECT pg_reload_conf()"` fails (both statements run in one transaction) — two `-c` calls.
25. `cloc --vcs=git` lists files of the repository in the **current directory** — run it from inside each repo (an absolute path
    argument from another repo yields 0 files).
26a. `ab_check_profile b` expects `GOMX_CARD_PREVIEW_CHARS=300` since 2026-10-24 — a B deployment from an older overlay fails the check (and measures full-body cards).
26b. Run the gomx CI replay container as root with `git config --global --add safe.directory /src`, or `check-generated-go.sh` fails on the mounted repo's ownership.
26. `~/.cache/gomx-gocache` is root-owned (written by a root container) — run the Go CI replay as root in the container, or use a
    fresh cache; uid-1000 runs fail with `permission denied` in seconds.

## 8. From v1 to a full comparison

State after the 2026-10-18 evening gap-closure session (`AB-TEST-GAPS-PROGRESS.md` has the stage-by-stage log;
`thesis/results/AB-COMPARISON-v1.md` §5.7 the h4/v2 definitions):

| Area | Status | Owner |
|---|---|---|
| Sign-off | Hubert reviews the alignment edits on his branch (N+1 fix, `seedbench.go` word bank) and signs `forum-spec.md` (incl. updating its §9 to the agreed protocol) | both |
| Journeys | **done (h4):** J4 (+image) and J9 on BOTH adapters, J8 receipt latency/loss probes; **A-11 social seed done (2026-10-24)** | A + B |
| Realism | **done (h4):** D4 shared 300-word Zipf bank in both seeders; D3 `--auth-reads` variant. **done (h5, 2026-10-24):** D1 `--real-spa` cold-cache waterfall variant; D2 card-excerpt parity. **Open:** a cache-model (warm) replay | both |
| Scaling | **done (h4):** `--hpa` = identical HPA 1–3 @ 70 % on both (verified by `ab_check_profile`) — measured run pending | both |
| Statistics | **done:** Welch / exact Mann-Whitney / Cliff's δ / Holm in every REPORT.md (`ab_stats.py`). **Open:** the N ≥ 10 round itself (`make ab-round ARGS="--plan demo:10,stress:3,capacity:3 --label v2"`, ≈ 6 h) | A |
| Client side | **done (tooling):** `lighthouse.sh --cluster` (mobile + desktop, both targets), Playwright suite on A (desktop + Android emulation). **Open:** the measured runs on the final images; RUM on the cluster | both |
| H3 | re-run trivy / cloc / artifact sizes / dev-loop on the final commits of both repos (dev-loop for B needs its toolchain) | both |
| Sensitivity | **done (tooling):** `--gc-variant`, `--relay-poll-ms`; measured runs pending | A + B |
| Resilience | **done (tooling):** `ab-chaos.sh` (pod-crash / pod-delete / db-restart), `soak` + `spike` profiles; runs pending | both |
| Infra | CI MinIO image mirror; keep the image backup | A |
| A hot path | **done (2026-10-24):** sargable `effective_mask` + cache fast path in `has_permission` (D10 reduced; see `AB-COMPARISON-v1.md` §5.8) | A |
| B hot path (optional, Hubert) | `GetCommunityBySlug` recomputes `member_count` with a COUNT per page view (0.19 ms); `Vote` sums all votes (`SumPostVotes`, 0.38 ms on the hottest post) instead of reading the trigger-maintained `vote_score`. **Done 2026-10-24 (for Hubert's review):** 499 for cancelled requests, hub queue 256, private-comment gate, card preview knob | B |
| D2 / D11 | **done (2026-10-24):** 300-char excerpt on both sides for measured runs (`GOMX_CARD_PREVIEW_CHARS=300`; B product default unchanged) | both |
| Mobile | real-device / emulator pass of the Tauri Android shells (B has a committed Android project; A has the desktop shell only) | both |

### 8.1 Runbook for the v2 round (next session)

1. Rebuild + deploy both images from the current trees (the seeders changed): A `make mk-deploy` (production
   posture, then `ab-round` re-applies the bench profile) or `scripts/deploy.sh`; B `make gomx-deploy ARGS=--reset-db`.
   Verify the new bank is live: `SELECT count(*) FROM forum_content.threads WHERE search_tsv @@ websearch_to_tsquery('simple','abandon')`
   on A ≈ 1 600 (rank 1) and `… 'zephyr'` ≈ 100 (rank 300); same counts ±10 % with ILIKE on B's `posts`.
2. Gate: `TARGET=a scripts/run-load-test.sh smoke` and `TARGET=b …` (both green, probes > 0).
3. Round: `setsid nohup make ab-round ARGS="--plan demo:10,stress:3,capacity:3 --label v2" > /tmp/round.log 2>&1 &`
   (never leave it attached to a Claude session — a dead session kills the job). ≈ 6 h.
4. Variants (each a separate, labelled round): `--auth-reads` (demo:5), `--hpa` (demo:5), `--gc-variant`
   (demo:3), `--relay-poll-ms 500` (demo:3), `--plan soak:1 --k6-env SOAK_MINUTES=60`, `--plan spike:3`.
5. Resilience: `make ab-chaos ARGS="--target a --fault both"` then `--target b`.
6. Client side: `make lighthouse-cluster ARGS="--target a --preset mobile"` (+ desktop, + target b), on a quiet
   box; `cd frontend && npm run test:e2e` against the local Development seed (recipe in `frontend/README.md`).
7. Report: every round's REPORT.md already carries the statistical tests; copy the v2 tables into
   `AB-COMPARISON-v1.md` as a new results section.
