# A/B test-gap closure — progress log (session 2026-10-18 evening)

**Purpose.** Resumable log of the session that closes the test gaps listed in `CAMPAIGN-REPORT.md` §8 /
`AB-START-HERE.md` §8 / the artifact review §10, so the *next* session can run the full measured round
(N ≥ 10) with the corrected harness. If this session dies, resume from the "Status" table below — every
stage says what landed, what is verified and what is still open.

Owner commits (Claude never commits). Everything here is in the working tree of `forum-dotnet`
(branch `31-feat-phase-13---ab-benchmark`) and `../gomx` (branch `feature/master-thesis`).

## 0. Starting point (verified at session start)

- Host rebooted ~21:50; minikube profile `forum` was **Stopped** → started again by this session
  (`minikube -p forum start`, log in the scratchpad). Docker up, images present
  (`gomx:bench`, `forum-dotnet-api/web`, MinIO backup tar in `~/.cache/forum-dotnet/images/`).
- `forum-dotnet` working tree: campaign folder + home-feed fix + owner-index migration + doc edits,
  **uncommitted** (snapshot: scratchpad `safety/forum-dotnet-worktree-*.patch`).
- `gomx` working tree: the anonymous-membership N+1 fix (`handlers_community.go` + 2 test files),
  **uncommitted** (snapshot: scratchpad `safety/gomx-worktree-*.patch`). `origin/master` moved to
  `e4b1d8d` (2026-10-18 09:34, "Merge branch 'feat/site-portal-templbook'") — `origin/feature/master-thesis`
  still `f03d46a`. `git fetch` needs GitLab credentials this shell does not have → the comparison below
  uses the remote-tracking refs already present locally.

## 1. Credibility review of the v1 round (what the numbers can and cannot carry)

Verdict unchanged from the campaign report: demo (N = 5) supports the tail-latency, DB-CPU, memory and
wire conclusions; stress/capacity (N = 1) are observations. Additional points found in this review:

| # | Issue | Effect | Action in this session |
|---|---|---|---|
| C1 | `ab_report.py` only flags CI overlap; the Welch / Mann-Whitney values in the artifact were computed by hand | reviewers cannot reproduce the p-values from the archive | add Welch t, exact Mann-Whitney U, Cliff's δ and Holm-corrected verdicts to `ab_report.py` (pure Python, no scipy on this box) |
| C2 | A's outbox relay polls every **5 s** (`Messaging:PollIntervalMilliseconds=5000`, also in the cluster) — a WS notification on A waits 0–5 s in the outbox before the hub sees it; B publishes to Redis in the request | J8 "receipt" is not measured at all in v1; this is the largest *architectural* realtime difference and it is invisible | J8 probe metric (receipt latency + loss) in both adapters; record the relay interval in the profile; optional variant with 500 ms |
| C3 | Neither seeder runs `ANALYZE` after the bulk insert; Postgres plans on a freshly reseeded DB depend on when autoanalyze happens to fire during the warm-up | run-to-run plan noise, asymmetric only by chance | `ANALYZE` on both sides inside `ab_reseed` (symmetric, cheap) |
| C4 | D4: 32-word bank, every word in nearly every body → J6 is a worst-case search on both sides and B's trigram+ILIKE recheck dominates its DB cost | distorts B's DB CPU, tails and the capacity knee | 300-word Zipf bank in BOTH seeders + k6 draws (mix v2) |
| C5 | D3: anonymous reads only | favours B (signed-in B pages add session + per-card queries) | `AUTH_READS=1` harness variant (reads carry the session/bearer), round flag `--auth-reads` |
| C6 | J4 (+image) and J9 (DM) have weight 0 because the B adapter lacks them | upload path (presigned vs server ingest) and DM path unmeasured | B adapter J4/J9, A adapter J9; mix v2 gives them weight |
| C7 | A's `GET /api/content/categories` lists private categories anonymously while B's `/communities` hides them | posture parity (F5) + `nav` does slightly different work | hide unreadable private categories in `ListCategories` + integration tests |
| C8 | A's access tokens live 15 min — any profile longer than ~12 min (soak) would 401 | blocks a soak profile | re-login on 401 in `targets/a.js` (counted) |
| C9 | No E2E through a real browser on A — the home-page regression passed 248 + 69 tests | functional gaps invisible to the gates | Playwright suite (desktop + Android emulation) on the Development seed |
| C10 | Lighthouse ran locally on demo data, desktop-ish, never on the cluster | client-side numbers not from the benchmark deployment | `scripts/lighthouse.sh --cluster` for A and B, mobile + desktop presets |
| C11 | No long-run, spike or fault-injection evidence | leaks / recovery behaviour unknown | `soak` + `spike` profiles in `main.js`, `scripts/ab-chaos.sh` (pod kill, DB restart) |
| C12 | HPA and GC policy deviations (A-15, D8) untested | scaling and memory claims rest on one configuration | `--hpa` and `--gc-variant` round modes |

## 2. Hubert's changes on `master` vs our edits (gomx, read-only comparison of the local remote-tracking refs)

- `origin/feature/master-thesis` = `f03d46a` (the deployed `gomx:bench` + our uncommitted N+1 fix on top).
  `origin/master` = `e4b1d8d` (2026-10-18 09:34): `feat: gomx site + portal`, `per-app security policy, HEAD,
  Rewrite proxy`, `key rate limits on trusted client IP` (`GOMX_TRUSTED_PROXIES=1` in the base yaml), `hx-boost
  on forum shell` (**browser-side behaviour change**: in-app navigation becomes HTMX partial swaps — affects real-
  browser/Lighthouse "second page" numbers, not the k6 adapter, which never sends `HX-Boosted`), `decouple
  app/forum, shared client behaviors, mobile offline boot page` (`app/forum/mobile/*`, Tauri Android/iOS configs
  now `frontendDist ../app/forum/mobile`, identifier `com.gomx.forum` — no longer matching the committed
  `gen/android` project, which still says `com.gomx.app`). 145 files, +9 956 / −1 711.
- **Our three uncommitted files do not conflict textually with master** (`handlers_community.go`: master only
  renames a metrics import; `web_test.go`: master only moves `gomx/internal/media` → `gomx/pkg/media`;
  the test file is untouched on master). `git merge origin/master` would still refuse to start until they are
  committed or stashed, and the branch merge itself conflicts in `server_builder.go`, `cmd/server/main.go`,
  `internal/server/router_test.go` and the `internal/server/forum.go` → `app/forum/app.go` rename
  (thesis-only `Version` / `friendPostNotificationsEnabled()` hunks). **Nothing was merged** — the bench image
  stays on the thesis branch; the merge is Hubert's call (and his conflicts to resolve).
- The HTTP contract the B adapter relies on is unchanged on master (`routes.go`, `post.templ`, `pages.templ`,
  `community.templ`, `csrf.go`, upload/status handlers). Thesis-only facts the adapter needs: page size 20,
  the A-shaped seed, `GOMX_COMPRESS`, `GOMX_NOTIFY_FRIENDS_ON_POST`, `GOMX_DB_MAX_CONNS`, the seed advisory lock,
  the bench overlay. A pure-master deployment would break `postableSlug()` (different private-space layout).
- Our edits on B in this session: `app/forum/shared/seedbench.go` (300-word Zipf bank, deterministic ranks —
  needed for D4; gofmt/vet/tests green in the CI-replay container, see Status). Hubert must review both the
  N+1 fix and the seeder change before pushing.

## 3. Mobile / Android (Hubert's request)

What B already has (from the exploration): a committed Tauri v2 **Android project** (`src-tauri/gen/android`,
`minSdk 24`, hosted-shell pointing at `http://10.0.2.2:8080`; no build script, no signing, no mobile CI job;
`docs/MOBILE.md` is stale), edge-to-edge safe-area CSS, Playwright `mobile-chrome` (Pixel 5) and
`mobile-safari` (iPhone 13) projects that run only locally (CI = Chromium desktop), and Lighthouse's default
**mobile** emulation (no preset) in `lighthouserc.json` — whose stored reports flag `meta-viewport`
(`user-scalable=no`) under accessibility.

What this session adds, feasible without an Android SDK/device/sudo and **paired on both sides**:
- `scripts/lighthouse.sh --cluster --target a|b --preset mobile|desktop` — the same two pages on the benchmark
  deployment through the ingress, one Lighthouse version, mobile = Moto G Power class + simulated slow-4G/4× CPU
  (what Lighthouse calls the mobile form factor; identical to B's own config), desktop = the built-in preset.
- Playwright for A (S7): a `desktop` and an `android` project (device emulation: viewport, DPR, touch, Chrome-for-
  Android UA) with layout/overflow/tap-target checks and the full browse/compose flows — the counterpart of B's
  `mobile-chrome` project, runnable against the local Development seed or the cluster.
- Not feasible here, listed as owner work: a real-device/emulator pass of either Tauri Android shell.

## Status

| Stage | What | State |
|---|---|---|
| S0 | Plan, progress file, cluster start | done (this file) |
| S1 | Statistics in `ab_report.py` (Welch, Mann-Whitney, Cliff's δ, Holm) | **done** — `scripts/ab_stats.py` (pure Python; reproduces the artifact's hand-computed p-values exactly: journey p95 Welch 0.0028 / MW 0.0079, app CPU 0.32 / 0.42, DB CPU < 0.001), hooked into `ab_report.py` ("## Statistical tests" + `stats.json`); v1 round report regenerated |
| S2 | J8 receipt latency + loss probes (both adapters) | **done + smoke-verified on the cluster** — `forum_ws_receipt` (write request start → matching push on the writer's socket), `forum_ws_probes_{sent,received,lost,unresolved}`, `probe_loss_pct` in the journeys block; smoke: A median **1 528 ms** (outbox relay poll 5 s) vs B **5 ms**, 0 % loss on both |
| S3 | J4 (B) + J9 (A, B) adapters; mix v2 | **done + smoke-verified** — A J4 = initiate → presigned PUT (256×256 RGBA PNG generated in k6, ≈262 KB) → commit → create thread with `![photo](image:<id>)` → attach; B J4 = `POST /uploads` (202) → status polls at 1 s → `POST /posts` with `![](/uploads/<id>)`; A J9 = open direct conversation once per VU + send; B J9 = `POST /messages/{friend}` (friends discovered per session from `/messages`); mix **v2** (J4 3 %, J9 4 %), harness **h4-2026-10-18** |
| S4 | Selective word bank (A `SeedText.cs`, B `seedbench.go`, k6 `assets.js`) | **written + tested**: A build green, `SeedFlowTests` (determinism, FTS) green; B `gofmt`/`go vet`/`go test ./app/forum/shared/... ./app/forum/web/...` green in the CI-replay container (`gomx-ci-go:local`, module cache now in `~/.cache/gomx-gocache/gopath`); B rebuilt (`gomx:bench-f03d46a-dirty`) + reseeded — **live selectivity on B's 1 600 posts: rank 1 `abandon` 1 600 (100 %), rank 50 `beacon` 613 (38 %), rank 100 `cousin` 295 (18 %), rank 300 `zephyr` 96 (6 %), old `kubernetes` 0** — matches the analytical prediction (39 / 20 / 6 %); A rebuilt (`forum-dotnet-api/web:git-57f63d0-dirty`, `scripts/deploy.sh --seed-benchmark`, pods rolled, the deployed web bundle now carries the home-feed fix + the e2e test ids) + reseeded — **live selectivity on A's 1 600 threads: `abandon` 1 600, `beacon` 614, `cousin` 331, `zephyr` 97, `kubernetes` 0** — same documents as B within ±10 % |
| S5 | `AUTH_READS` variant + re-login on 401 + `ANALYZE` in reseed | **done** — `ANALYZE` on both sides in `ab_reseed`; `AUTH_READS=1` (A bearer / B cookie on every read, recorded in `variant.auth_reads`); A re-login on 401 (`forum_relogins`) |
| S6 | A: hide unreadable private categories + integration tests | **done** — `ListCategories.cs`, `GetCategory.cs`, `ContentReadAccess.ReadInfo`; `ContentFlowTests` private-category test extended (anonymous/outsider: absent from the list + 404 by slug; owner/moderator: listed + 200); 26 Content unit + 11 integration (Content/Seed/Realtime) tests green |
| S7 | Playwright E2E (desktop + Android emulation) for A on the Development seed | **done** (subagent): `frontend/playwright.config.ts` (projects `desktop` = Desktop Chrome, `android` = Pixel 7; portable Chrome for Testing 151 via `executablePath`, no `playwright install`), `frontend/e2e/{helpers,home-anonymous,browse,auth-compose,mobile-layout}.spec.ts`, 3 `data-testid`s (`thread-card`, `author-stats`, `profile-stats`), `npm run test:e2e` / `test:e2e:android`, README section, `.gitignore`. Final run: **24 passed · 8 skipped by design · 2 expected failures** (26 s). Regression coverage PROVEN: the pre-fix `use-home-feed.ts`/`feed-merge.ts` built from HEAD fails the two private-category tests ("SIGNAL LOST"), the fixed build passes. Note: because S6 now hides private categories from anonymous lists, the spec forces the 404 path via a `page.route` patch of the category list (real feed 404) and via a mock-free member-logs-out journey. **Mobile product bugs found on A (pinned as `test.fail`):** signed-in TopNav needs ≈ 532 CSS px on a 412 px phone (viewport widens, user menu + compose modal cut off); sub-40 px tap targets (Post comment 32.8, + New thread 32.8, icon buttons 36, card like 19.2, REPLY 17.6, tag chip 29.2 …); TagPicker Enter-within-debounce race. Hubert's B suite uses Pixel 5 (local-only) — same class of device |
| S8 | Lighthouse on the cluster (mobile + desktop), both targets | **done + run once** (validation conditions — pre-fix A web image, v1 seed, concurrent Playwright agent on the host): mobile feed perf **A 0.56 vs B 0.89** (TBT 776 vs 34 ms, LCP 2.8 vs 2.2 s), mobile thread 0.85 vs 0.94, desktop 0.97/0.99 vs 1.00/1.00; archive `thesis/results/lighthouse-cluster-20261018/` (README + LHR JSON). Chrome's `--host-resolver-rules` value must be quoted inside `chromeFlags` (fixed). Re-run on a quiet box after the image rebuild |
| S9 | soak / spike profiles, `ab-chaos.sh` | **written** — `soak` (SOAK_MINUTES/SOAK_VUS, pass/fail thresholds), `spike` (phase-tagged base/spike/recover ×2, recovery-vs-baseline p95), `scripts/ab-chaos.sh` + `ab_chaos_report.py` (pod-crash / pod-delete / db-restart / both; per-5-s timeline from the k6 CSV stream, failed requests + recovery time per fault; `make ab-chaos`); live runs pending |
| S10 | `--hpa`, `--gc-variant`, relay-interval knobs in `lib-bench.sh` / `ab-round.sh` | **done + verified live**: with `AB_HPA=true AB_GC_VARIANT=true AB_RELAY_POLL_MS=500` both deployments got the identical HPA (1–3 @ 70 %, behavior block), `DOTNET_gcServer=0` / `GOMEMLIMIT=384MiB` / `Messaging__PollIntervalMilliseconds=500`, `ab_check_profile` all-ok on A and B; reverting to the default profile removed the HPAs and the env overrides, check all-ok again |
| S11 | Docs: `AB-START-HERE.md` §8, `AB-COMPARISON-v1.md` deviations, `CLAUDE.md`, memory | `AB-COMPARISON-v1.md` §5.7 (h4/v2 table) + §6.2 (D3/D4/D8/D-9 updated, D12–D14 added) and `AB-START-HERE.md` §8 + §8.1 runbook **done**; `CLAUDE.md` + memory pending (end of session) |

## How to resume

1. `minikube -p forum status` — start it if stopped; pods self-heal in 2–3 min. Both apps are deployed from the
   current trees (A `git-57f63d0-dirty` rebuilt 2026-10-19 ~00:00 with the new seeder + the web fix; B
   `gomx:bench-f03d46a-dirty` with the new seeder) and seeded with the Benchmark profile + the new word bank.
   The cluster is in **bench shape** after the chaos runs (no HPA, tracing off; A's limiter restored by the
   EXIT trap; `make mk-deploy` restores the production posture).
2. Read this file's Status table and §4; each stage records the files touched and the verification done.
3. Backend gates: `cd backend && dotnet build Forum.slnx && dotnet format Forum.slnx --verify-no-changes && dotnet test Forum.slnx` (248 green on 2026-10-19).
4. Harness smoke: `TARGET=a scripts/run-load-test.sh smoke` and `TARGET=b …`; frontend: `cd frontend && npm run typecheck && npm run lint && npm test`, e2e per `frontend/README.md`.
5. The v2 round and the variant/chaos/Lighthouse runs: `AB-START-HERE.md` §8.1.
6. Owner to-do before the round: commit both trees (Hubert reviews the two gomx files), optionally merge
   Hubert's `master` into `feature/master-thesis` (conflicts listed in §2) and rebuild B if so.

## 4. Final gates of the session (filled in as the validation chain finishes)

Validation chain started 2026-10-19 ~00:15 (`scratchpad/chain.sh`): h4 smokes on the NEW images (A, B) → full
backend test suite → `ab-chaos.sh --target a --fault both` → the same on B. Results below.

- **h4 smoke on the new images (5 VUs, 60 s, both targets):** A 1 188 requests, p95 18 ms, 0 % failed, J4 23 /
  J9 32 / J8 4 holds, receipt probes 8/8 (median 200 ms, max 279 ms — the relay drains continuously while the
  outbox is busy; the 1.5 s median of the first smoke was an idle-phase sample: A's receipt latency is
  bimodal, 0.2–5 s depending on relay phase, which the demo round's ~280 probes will show as a distribution);
  B 740 requests, p95 20 ms, 0 % failed, J4 22 / J9 22 / J8 4, probes 8/8 (median 6 ms, max 13 ms).
- **Full backend suite on the final tree: 248 passed / 0 failed / 0 skipped** (Content 26, Social 25,
  Engagement 24, Files 42, Api 53, Architecture 2, Identity 30, Integration 46 — 2 min 13 s on Testcontainers);
  `dotnet build` 0 warnings; `dotnet format --verify-no-changes` clean.
- **Chaos validation, A** (`thesis/results/chaos/20261018-2325-A-both-validate/`): pod-crash of one app pod →
  **0 failed requests**, recovery 18 s, max p95 439 ms; `postgres-0` restart → **4 991 failed** (8.6 % of the run),
  recovery 82 s, max p95 7.5 s (readiness gates on the DB → both pods unready → 503s until the pool reconnects);
  WS errors 20, probe loss 15 % (30/200), receipt p95 2.4 s. The script, the CSV stream, the timeline and the
  report all work end-to-end; the report now counts only the `http` scenario (setup/probe rows excluded).
- **Chaos validation, B** (`thesis/results/chaos/20261018-2337-B-both-validate/`): pod-crash → **0 failed**,
  recovery 9 s, max p95 387 ms; `postgres-0` restart → **2 373 failed** (6.6 %), recovery 71 s, max p95 1.5 s; WS
  errors 20, probe loss 13.5 % (27/200), receipt p95 173 ms. Summary + side-by-side in `thesis/results/chaos/README.md`.
- **Harness refinement after the chaos runs:** probe outcomes are now `received` (≤ timeout) / `late` (push after
  the timeout — delivered, slow) / `lost` (never pushed within the hold) / `unresolved`; `probe_late_pct` joins
  the journeys block and the REPORT.md WS table. Needed to tell A's durable-but-delayed replay from a real loss.
- **ab-round dry run** (`--plan smoke:1 --label h4-dryrun`) started 23:49 to validate the orchestrator with the h4
  harness end-to-end (reseed + ANALYZE, warm-up, measured smoke, idle window, variants recorded, REPORT with the
  statistical section) — result appended below when it finishes.
- **ab-round dry run done** (`thesis/results/rounds/20261018-2349-h4-dryrun/`): both smoke runs valid (reseed +
  ANALYZE, 2 fresh pods, warm-up, measured window, idle window, limiter restored), `round.json.variants` recorded,
  REPORT.md carries the per-journey rows incl. J4/J9, the WS table with the receipt-probe rows and the statistical
  section (n = 1 → `n<3` verdicts, as designed). The orchestrator is ready for `--plan demo:10,stress:3,capacity:3`.

## 5. What was deliberately NOT done (owner / next session)

- The measured v2 round itself and the variant / soak / spike / chaos repeats (≈ 6 h + ≈ 3 h; §8.1 of AB-START-HERE).
- D1 (real SPA waterfall replay with a cache model) — needs a browser-recorded HAR of each page on the cluster and
  a replay adapter; the Playwright suite now gives a place to record it from.
- Merging Hubert's `master` into the thesis branch (conflicts outside our files) and any B code change beyond the
  seeder bank: the 499-for-cancelled-requests fix, the hub's slow-consumer policy, the `member_count`/`SumPostVotes`
  hot-path items stay Hubert's.
- B's dev-loop benchmark (needs Go/bun/task on the host), B's CI replays beyond go test (Playwright e2e,
  Lighthouse CI, govulncheck, Sonar), A's frontend CVE bumps (13 fixable, toolchain only), D10 (`effective_perm_cache`
  read), a real-device / emulator pass of the Tauri Android shells, Redis on A.
- Lighthouse cluster numbers were taken under validation conditions (concurrent Playwright agent) — re-run quiet.
