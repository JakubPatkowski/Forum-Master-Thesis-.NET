# A/B test campaign report — forum-dotnet (A) vs gomx (B), night of 2026-10-17/18

> Campaign folder: `thesis/results/campaign-20261017-2357/` (this file: `05-analysis/CAMPAIGN-REPORT.md`; step log:
> `PROGRESS.md`; artifact index: `index.json`). Every number below is copied from an archived file named next to it —
> primarily `thesis/results/rounds/20261018-0051-v1/REPORT.md` (also copied to `03-round/`). Methodology, hypotheses,
> fairness invariants F1–F8 and the deviation register D1–D11 / D-x: `thesis/results/AB-COMPARISON-v1.md` (§9 now holds
> the round tables verbatim). Statistics: mean ± 95 % CI (Student t over the valid repeats); *distinct* = the two CIs do
> not overlap, *overlap* = this round cannot separate the means. Ratios are B/A.

## 0. Version under test (Phase 0 — `00-preflight/`)

| | A (React SPA + .NET 10 modular monolith) | B (Go + templ/HTMX SSR monolith) |
|---|---|---|
| repo / branch | `~/projects/forum-dotnet`, `31-feat-phase-13---ab-benchmark` | `~/projects/gomx`, `feature/master-thesis` |
| HEAD + working tree | `57f63d0` + uncommitted changes (`A-working-tree.patch`, 95 KB: migration `AddOwnerActivityIndexes`, lib-bench/ab-round/report edits, docs) | `f03d46a` + uncommitted `handlers_community.go` (+2 test files) (`B-working-tree.patch`, 4.5 KB) |
| deployed image | `forum-dotnet-api:git-57f63d0-dirty` sha256 `a54db7e1…`, `forum-dotnet-web:git-57f63d0-dirty` sha256 `cd916f5f…` (built 2026-10-17 23:25 / 23:28 CEST) | `gomx:bench` = `gomx:bench-f03d46a-dirty` sha256 `80140d5f…` (23:25 CEST); `/stats` version `f03d46a-dirty` |
| images = working trees? | yes — every modified source file has mtime ≤ 23:17 CEST (before the builds); nothing rebuilt during the campaign | yes |
| DB schema | 20 EF migrations in `forum_net`, last `20261017211738_AddOwnerActivityIndexes`; both partial owner indexes present | goose migrations at boot |
| Postgres | `PostgreSQL 18.4 (Debian 18.4-1.pgdg13+1)` | identical |
| harness | `load/k6` mix `v1`, harness `h3-2026-10-17`, k6 v2.1.0 | same harness, adapter `targets/b.js` |
| common profile | 34/34 checks ok on both (`ab_check_profile.txt`): 2 fixed replicas, no HPA, 250m/256Mi → 1 CPU/512Mi, probe timeouts 5 s, tracing off, limiters neutralized (A 1 000 000 permits; B `GOMX_AUTH_RATELIMIT=0`), pool 30, `postgres:18` with the same args/resources (1 CPU / 1 GiB) | |

Decisions carried into this campaign and NOT reopened (restated next to the numbers they affect): **D2/D11** B's feed/search
cards keep the whole rendered body; **D3** reads stay anonymous; **D4** the 31-word search bank stays; **D10** A keeps live
ACL evaluation (`has_permission`); Hubert's optional B hot-path optimizations (member_count COUNT, vote sum) are NOT applied.

## 1. Functional tests (Phase 1 — `01-functional/`, `functional-summary.json`)

| gate | A | B |
|---|---|---|
| build / static checks | `dotnet build Forum.slnx`: 0 warnings, 0 errors | `go vet` clean; golangci-lint v1.64.5 clean; generated code (sqlc/templ/story imports/i18n) up to date; gofmt + `templ fmt` clean |
| tests | **248 passed / 0 failed / 0 skipped** (8 projects; 46 integration tests on Testcontainers Postgres 18 + MinIO + RabbitMQ, 4 m 13 s) — TRX in `A/trx/` | `go test -race ./...`: **378 passed / 0 failed / 25 skipped** (the 25 = Postgres-adapter + Redis-bus tests that skip without DATABASE_URL/REDIS_ADDR, exactly as in the CI fast lane); statement coverage 30.5 % |
| adapter / broker lanes | (covered by the integration suite) | postgres lane **81 / 0 / 0** (72.6 % coverage, postgres:18-alpine service as in CI); redis lane 1 passed |
| formatting / lint | `dotnet format --verify-no-changes` clean | `lint:ts` (bun 1.3.14): `bun install --frozen-lockfile` 909 packages, twmerge cache up to date, eslint, `tsc --noEmit`, prettier — all clean |
| frontend | `tsc --noEmit` ok, `next lint` ok, vitest **69 passed**, `next build` ok on the first attempt | (SSR: assets are part of the image) |
| verdict | **PASS** | **PASS** |

Not replayable here: B's `e2e:playwright` + `lighthouse` CI jobs (prebaked Playwright 1.58.2 + bun registry image), `govulncheck`,
Sonar. `verify:container` ≙ the `gomx:bench` build of this working tree (same `ContainerFile.dev`). Hubert's failing GitLab
pipeline on `f03d46a`: `lint:ts` reproduces **green**; the CI image's Go 1.26.9 satisfies `go 1.26.1` with `GOTOOLCHAIN=local`
(no toolchain download); MR-lane jobs (e2e, verify:container) cannot be replayed without the registry image → the job log is
still needed. Environment note: the first B pass ran the Go steps as uid 1000 and failed on a root-owned
`~/.cache/gomx-gocache` in ≤ 8 s; the rerun as root with identical commands is the recorded result; the B tree was left
unchanged by the gates.

## 2. Live checks through the ingress (Phase 2 — `02-live-checks/SUMMARY.md`)

After a pristine reseed (800/1600/9000/15000, fresh pods on both): every harness endpoint answers as the adapters expect;
TLS 1.3 + HTTP/2 + gzip from the shared ingress on both; A private content → 404 anonymously, 0 private rows in anonymous
search (268 private threads in the DB); A WebSocket ticket single-use (101 then 401); B OAuth mock off (303
`/login?error=oauth_unavailable`, no session cookie), private community/post → 404; health 200 on both; Prometheus 19/19
targets up; no errors in either app's logs. Wire sizes (gzip / raw): A feed page 1.6 / 8.0 KB vs B space page 16.2 / 174.1 KB
(**D2/D11**); A comment tree of the 236-comment thread 22.8 / 103.5 KB vs B thread page 45.0 / 745.3 KB. SQL statements per
journey counted on the live DBs reproduce AB-COMPARISON-v1 §5.6 (B `nav` = 1 statement — the anonymous-membership fix is
live). Idle DB chatter, 2 replicas, no traffic: A 188 vs B 116 statements per 20 s (A: 5 outbox relays × 2 replicas every
5 s, each in BEGIN/ROLLBACK + the 7-statement Npgsql reset; B: River `JobGetAvailable` + pgx pings + leader election).

Functional finding (A, not on the harness path): the anonymous SPA home page `/` failed with "Category not found" because
the home feed merges all 12 categories and the A-12 read gate answers 404 for the 4 private ones (`screenshots/A-home.png`).
Per-category, thread, search and profile pages render correctly. Fixed in the working tree after the round — see §6.

## 3. The measured round (Phase 3 — archive `thesis/results/rounds/20261018-0051-v1/`, copies in `03-round/`)

Gate `20261018-0019-gate` (smoke:1): both runs valid, 0 % failed. Round `20261018-0051-v1`: plan `demo:5,stress:1,capacity:1`,
ABBA order, reseed + fresh pods + 60 s warm-up (~6 % writes) before every run, 00:51 → 03:43. **14/14 runs valid**: no pod
restarts, no 429, 0.00 % failed requests in every run, common-profile check passed before every run. A first attempt
(`20261018-0029-v1-ABORTED-session-crash`) died with the operator session during its second run (killed hard, not a cluster
fault) and is excluded; the relaunch ran detached from the session. Host during the round: nothing else running (compose
stopped, no builds/tests/tunnels); node-exporter memory: swap use flat at ≈ 1.5 GiB (pre-existing, Δ ≤ 40 MiB in any run),
MemAvailable ≥ 1.2 GiB (minimum during A's capacity run, k6 RSS 2.3 GiB). k6 load generator (same host): max 44 % / 74 % of
one core in demo (A / B), 110 % / 102 % in stress, 127 % / 101 % in capacity — on an 8-core host with node busy ≤ 4.3 cores,
i.e. the generator was not saturated.

### 3.1 H1 — demo (closed model: 0→10→40→80 VUs over 7 min, 20 WS VUs; 5 repeats per side)

Offered load is fixed by the closed model, so journeys/s is near-equal by design (A 91.55 ± 0.13, B 90.05 ± 0.74 — B's
slightly lower rate is its longer think-time-plus-latency per iteration); the comparison lives in latency and resources.

| metric (demo, 5 + 5) | A | B | B/A | CIs |
|---|---|---|---|---|
| journey latency median (ms) | 8.4 ± 0.7 | 8.0 ± 1.2 | 0.95× | overlap |
| journey latency p95 (ms) | 19.2 ± 3.1 | 60.8 ± 18.3 | 3.16× | distinct |
| HTTP request p95 (ms) | 11.4 ± 1.3 | 55.6 ± 16.0 | 4.86× | distinct |
| HTTP requests per run | 76 789 ± 247 | 41 828 ± 341 | 0.54× | distinct — A's SPA pattern = 1.8 requests per journey (D1: idealized client, lower bound) |
| KiB received per journey (wire, gzip + TLS) | 2.21 ± 0.01 | 11.63 ± 0.05 | 5.25× | distinct — **D2/D11**: B's cards carry the whole rendered body |
| failed requests (%) | 0.00 | 0.00 | – | |

Per-journey latency (ms), demo: medians favour B on the single-request pages (J2 thread 9.6 vs **4.6**, J5 reaction 7.4 vs
**4.6**, J7 profile 7.0 vs **4.6**; J3 comment 6.6 vs 5.4 overlap; nav 3.0 vs 3.4 overlap) and A on the heavy ones (J1 feed
**8.0** vs 11.8 — D2; J6 search **11.0** vs 40.8 — the trigram/ILIKE vs tsvector paradigm under the non-selective bank D4);
every p95 and p99 favours A, 2.5–10× (J6 p95 18.6 vs 148.9; J5 p99 40.7 vs 142.2; J1 p99 36.4 vs 111.6) — all *distinct*
except J2 p95 (20.0 vs 32.0, overlap). A's medians include 2–3 sequential HTTP round trips per journey (D1/D9), B's one.

| resources over the scenario window (demo, 5 + 5) | A | B | B/A | CIs |
|---|---|---|---|---|
| app CPU per journey (ms) | 5.017 ± 0.294 | 4.838 ± 0.360 | 0.96× | overlap |
| app **MARGINAL** CPU per journey (ms, idle cost removed) | 4.882 ± 0.293 | 4.765 ± 0.365 | 0.98× | overlap |
| app idle cores (no traffic, 2 replicas) | 0.012 ± 0.005 | 0.007 ± 0.002 | 0.53× | overlap |
| app memory avg / max (MiB, 2 replicas) | 390 ± 5 / 405 ± 7 | 66 ± 2 / 84 ± 2 | 0.17× / 0.21× | distinct — **D8**: .NET Server GC vs Go defaults |
| DB CPU per journey (ms) | 2.685 ± 0.162 | 4.551 ± 0.393 | 1.69× | distinct — B's search (ILIKE/trigram recheck), member_count COUNT, SumPostVotes (not applied optimizations); A's `has_permission` (**D10**) and Npgsql reset (**D9**) are inside A's number |
| DB memory avg (MiB) | 152 ± 12 | 196 ± 5 | 1.29× | distinct |
| bus CPU per journey (ms) — A RabbitMQ / B Redis | 1.519 ± 0.125 | 0.141 ± 0.008 | 0.09× | distinct — D-5 |
| bus idle cores / memory (MiB) | 0.101 ± 0.055 / 91 ± 7 | 0.007 ± 0.005 / 7 ± 0 | 0.07× / 0.08× | distinct |
| A frontend pod memory (MiB; idle SPA shell server, D-15) | 59 ± 1 | – | | |
| WHOLE namespace CPU per journey (ms) | 9.247 ± 0.538 | 9.544 ± 0.751 | 1.03× | overlap |
| WHOLE namespace MARGINAL CPU per journey (ms) | 7.884 ± 0.786 | 9.315 ± 0.808 | 1.18× | overlap |
| WHOLE namespace idle cores (no traffic) | 0.125 ± 0.062 | 0.021 ± 0.009 | 0.17× | distinct — A's standing cost (RabbitMQ, 10 relay loops, frontend) |
| WHOLE namespace memory avg (MiB) | 772 ± 11 | 357 ± 5 | 0.46× | distinct |
| ingress-nginx CPU per journey (ms, shared edge; compression happens here) | 1.484 ± 0.099 | 2.668 ± 0.147 | 1.80× | distinct — D2/D11 bytes |
| node busy cores avg | 2.97 ± 0.12 | 2.93 ± 0.15 | 0.99× | overlap |

WebSocket holds (J8; 20 VUs, 120 s cycles): subscribed 80 / 80, notifications 34 467 ± 3 055 (A, per-view subscription) vs
83 462 ± 2 035 (B, broadcast to every socket — D-7), errors 0 / 0.

### 3.2 Stress (closed model: 50→100→150 VUs over 6.5 min, 40 WS VUs; 1 + 1, informational)

| metric (stress) | A | B | B/A |
|---|---|---|---|
| HTTP journeys / s | 189.60 | 155.95 | 0.82× (closed model: B's longer latency lowers its iteration rate) |
| journey latency median / p95 (ms) | 10.0 / 41.0 | 54.0 / 454.0 | 5.4× / 11.1× |
| J6 search median / p95 (ms) | 12 / 33 | 209 / 1 493 | 17× / 45× — D4 + paradigm (D-3/D-4) |
| app CPU per journey, marginal (ms) | 4.367 | 4.527 | 1.04× |
| DB CPU per journey (ms) | 2.570 | 4.882 | 1.90× |
| WHOLE namespace CPU per journey, total / marginal (ms) | 7.880 / 6.942 | 9.555 / 9.460 | 1.21× / 1.36× |
| app / whole-namespace memory avg (MiB) | 392 / 883 | 97 / 482 | 0.25× / 0.55× (D8) |
| WS holds: subscribed / notifications / errors | 160 / 129 446 / **0** | **308** / 259 426 / **148** | B's hub closes slow consumers (§6) |

### 3.3 H2 — capacity (open model: 25…500 journeys/s in 8 steps of 15 s ramp + 45 s hold, ≤ 300 VUs, 20 WS VUs; 1 + 1)

| step | offered j/s | A achieved | A p95 ms | A DB cores | A app cores (2 pods) | B achieved | B p95 ms | B DB cores | B app cores (2 pods) |
|---|---|---|---|---|---|---|---|---|---|
| c01 | 25 | 25.0 | 13 | 0.05 | 0.16 | 25.0 | 30 | 0.08 | 0.09 |
| c02 | 50 | 50.0 | 12 | 0.10 | 0.17 | 50.0 | 32 | 0.17 | 0.16 |
| c03 | 100 | 100.0 | 14 | 0.21 | 0.40 | 100.0 | 34 | 0.34 | 0.34 |
| c04 | 150 | 150.0 | 16 | 0.32 | 0.51 | 149.6 | 158 | 0.59 | 0.52 |
| c05 | 200 | 199.3 | 34 | 0.50 | 0.74 | 199.6 | 355 | 0.83 | 0.73 |
| c06 | 300 | 300.0 | 77 | 0.66 | 1.02 | **209.1** | 3 210 | **0.99** | 0.81 |
| c07 | 400 | 393.2 | **595** | **0.93** | 1.09 | 204.4 | 2 895 | 1.00 | 0.70 |
| c08 | 500 | 404.5 | 1 658 | 0.92 | 1.08 | 203.8 | 3 507 | 1.00 | 0.72 |

(achieved/p95 from REPORT.md; per-step DB/app cores from Prometheus over each step's 45 s hold, `03-round/capacity-per-step-cpu.json`.)

**Sustained throughput** (highest step before the knee = p95 > 500 ms, failures > 1 % or achieved < 95 % of offered):
**A 300 j/s, B 200 j/s**; knees A c07 (400 offered, p95 595 ms, still 98 % achieved), B c06 (300 offered, achieved plateaus
at ≈ 205 j/s, p95 3.2 s). Both knees reached, both with 0 % failed requests (no errors, only queueing). The per-step CPU shows
**what saturates: the single-core Postgres on both sides** — B's DB reaches 1.00 core at c06 and stays there while its app pods
sit at 0.7–0.8 of their 2 cores; A's DB reaches 0.93 core at c07 with its app at 1.09 of 2 cores. B hits the same wall at half
the throughput because its DB cost per journey is 1.7–1.9× A's (§3.1), which under the v1 mix is dominated by J6 search (D4,
trigram/ILIKE recheck of every body) and the per-page `member_count` COUNT / per-vote `SumPostVotes` that were deliberately not
optimized. Over the whole capacity window: app CPU per journey 3.86 vs 4.36 ms (marginal 3.81 vs 4.29), DB 2.43 vs 4.62 ms,
whole namespace 7.13 vs 9.08 ms, app memory 390 vs 103 MiB (max 424 vs 201), wire 2.28 vs 11.72 KiB per journey; WS holds:
A 80 subscribed / 0 errors, B 222 subscribed / 152 errors (§6).

## 4. Static metrics (Phase 4 — `04-static/STATIC-SUMMARY.md`, `static-summary.json`)

| H3 metric | A | B | vs 2026-10-17 snapshot |
|---|---|---|---|
| trivy 0.72.0 fs scan, HIGH/CRITICAL with a fix | **13** (2 CRITICAL, 11 HIGH) in 10 distinct packages — all in the frontend toolchain: next@15.5.20 (×5), sharp (×3), nanoid (×2), postcss (×2), source-map-js (×1); 0 secrets; 5 k8s misconfigs (KSV-0014 ro-rootfs ×3, KSV-0118 ×2) | **0** vulnerabilities; 0 secrets; 5 misconfigs (KSV-0014 ×2, KSV-0118 ×2, DS-0002 image user root ×1) | A unchanged (13/13); B 0/0; misconfigs A 7 → 5, B 1 → 5 (B's bench overlay manifests are new scan surface) |
| cloc 2.06 — core application code (lines of code) | **52 097** (C# 23 166, TypeScript 10 652, JSON 10 305, CSS 4 666 …) in 789 files | **30 088** (Go 18 403, TypeScript 2 846, Templ 2 641, TOML 1 393, SQL 1 163 …) in 295 files | core A 51 745 → 52 097 (+352), B 28 755 → 30 088 (+1 333: the alignment commits B-01…B-14 + bench seed/placement); whole-repo growth = archives/docs |
| first-load web assets (gzip) | JS 194 901 B + CSS 15 801 B = **210 702 B** (raw 644 653 + 69 040) | styles.css 31 472 + forum.js 70 639 + bridge.js 21 421 + bridge-alpine.js 21 718 = **145 250 B** gzip (raw 554 760), embedded in the binary; server binary 39 948 450 B (13.2 MB gzip) | A unchanged (210 690 → 210 702) |
| container images (minikube daemon) | api **178 769 346 B** + web **243 475 415 B** | **41 188 802 B** (one image) | A +26 KB api, web identical; B +16 KB |
| dependency surface | 42 NuGet versions pinned centrally (24 distinct referenced in src); npm 9 deps + 14 devDeps (665 packages in package-lock) | 29 direct + 105 indirect Go modules (227 in go.sum); bun 9 deps + 27 devDeps (1 320 packages in bun.lock) | — |
| Lighthouse (lab, LOCAL, demo-scale data; same budgets) home `/` | perf **0.84**, a11y 0.95, bp 0.96, seo 1; LCP 4 135 ms, FCP 1 069, TBT 172, CLS 0.010 (working-tree API + home-feed fix; a first attempt against the old compose API scored 0.75 — kept as `lighthouse-A-attempt1-old-compose-api/`) | perf **0.92**, a11y 0.87, bp 1, seo 1; LCP 1 891 ms, FCP 938, TBT 27, CLS 0.177 | A 0.80 → 0.84, B 0.91 → 0.92 |
| Lighthouse thread page | perf **0.81**; LCP 4 007 ms, FCP 1 070, TBT 279 | perf **0.99**; LCP 2 089 ms, FCP 1 056, TBT 5 | A 0.81 → 0.81, B 0.99 → 0.99 |

Exact counts per metric (incl. Go module numbers) are in `04-static/STATIC-SUMMARY.md`. Not run: the dev-loop benchmark (B's
half needs the Go/bun/task toolchain on the host) and Lighthouse against the cluster on the Benchmark dataset (v2).

## 5. What the data supports (and does not)

**H1 — resources per served journey at equal load (demo, N = 5 per side):**
- *App CPU per journey is statistically indistinguishable* (5.02 ± 0.29 vs 4.84 ± 0.36 ms; marginal 4.88 vs 4.77; CIs overlap).
  The architectures spend the same CPU in their application tier for the same user-visible work — with A's adapter being an
  idealized client (**D1**, A's cost is a lower bound) and B rendering 20 full bodies per feed/search page (**D2/D11**, B's cost
  includes work A's cards do not do).
- *The DB tier is where they differ:* B spends 1.69× A's Postgres CPU per journey (distinct), A spends 10× B's bus CPU
  (RabbitMQ vs Redis; distinct) and carries a 6× larger standing cost (whole-namespace idle 0.125 vs 0.021 cores; distinct).
  Whole-namespace CPU per journey ends up equal in total (9.25 vs 9.54, overlap) and favours A marginally (7.88 vs 9.32,
  overlap — not separable at N = 5).
- *Memory is clearly different:* app 390 vs 66 MiB, whole namespace 772 vs 357 MiB (distinct) — **D8** applies (.NET Server GC
  vs Go GOGC defaults); a sensitivity run (workstation GC / `DOTNET_GCConserveMemory`, `GOMEMLIMIT`) is still owed.
- *Transfer:* 5.25× more bytes per journey on B (distinct) — **D2/D11** (full bodies in cards) plus HTML vs JSON (D-1).
- *Latency at equal concurrency:* medians favour B on single-request pages (fewer round trips, D1/D9), A on feed and search
  (D2, D4/paradigm); every tail (p95/p99) favours A 2.5–10× — B's tails are search-driven and widen with load (stress p95 454 vs
  41 ms). The tails are measured with `distinct` CIs; the medians mostly overlap or favour B.
- *Not supported:* a claim that one architecture needs less CPU in the app tier; the N = 5 CIs do not separate 5.0 from 4.8 ms.

**H2 — throughput ceiling (capacity, N = 1 per side):** A sustains 300 j/s and saturates at ≈ 400 j/s; B sustains 200 j/s and
saturates at ≈ 205 j/s. The limiting resource is the 1-CPU Postgres on both sides (per-step cores), so the 2× ceiling
difference is the DB-cost-per-journey difference, i.e. the search paradigm under D4 plus B's not-applied hot-path
optimizations — not an app-tier property. With N = 1 and a shared host this is an observation, not a tested hypothesis;
a repeat (N ≥ 3) and a selective word bank (D4 → v2) are needed before the ceiling is quoted as a result.

**H3 — static:** A carries 13 fixable HIGH/CRITICAL CVEs in its frontend toolchain (next/sharp/nanoid/postcss/source-map-js —
all third-party, all with available upgrades), B 0; A has 1.7× B's core code (two runtimes: C# + TypeScript vs Go + templ), two container images of 179 + 243 MB vs one of 41 MB, 211 KB vs 145 KB of gzip web assets; lab Lighthouse on
demo data favours B (perf 0.92/0.99 vs 0.84/0.81; LCP ≈ 1.9–2.1 s vs ≈ 4.0–4.1 s; TBT 5–27 vs 172–279 ms — the CSR hydration cost), with the cross-origin caveat D-18 on A's audit.

## 6. Anomalies and findings

1. **A SPA home page regression (functional, not measured).** Anonymous `/` renders the error state on any dataset with a
   private category: `useHomeFeed` merged every category from `GET /api/content/categories` (which returns private ones too)
   and the A-12 read gate's 404 rejected the whole `Promise.all`. Fix applied in the working tree at 03:47 (after the round):
   `feed-merge.ts` `exhaustSource()` + `use-home-feed.ts` treats 404/403 per source as "nothing to contribute"; regression test
   `feed-merge.exhaust.test.ts` (2 tests); `tsc`, vitest (71), `next lint` green; patch `A-frontend-home-feed-fix.patch`.
   Verified on the local production build against the Development seed (which has `private-club`): `A-home-after-fix-local.png`
   (before: `02-live-checks/screenshots/A-home.png`). The **deployed** web image still has the bug — rebuild on the next
   `make mk-deploy`. Secondary observation: the category list endpoint discloses private categories' names/slugs to anonymous
   users (the SPA's sidebar shows them) — the A-12 gate covers feed/detail/tree/search/user lists, not the list itself.
2. **B WebSocket holds break under load.** Stress: 308 subscribed / 148 errors (A 160 / 0); capacity: 222 / 152 (A 80 / 0);
   demo: 0 errors on both. B's hub drops slow consumers by design (`app/forum/hub/hub.go`: "client too slow to keep up" →
   `StatusPolicyViolation`) — under CPU saturation the k6 holders fell behind the broadcast (every socket receives every
   post/comment/vote fragment: 259 k notifications to 40 sockets in stress) and were closed; the harness reconnected them.
   Not an error for the HTTP results (0 % failed), but J8 "receipt" on B is lossy under load — a real-browser client would
   see dropped live updates. To report to Hubert; v2: measure J8 receipt latency/loss explicitly.
3. **B returned 500 for client-cancelled requests** (8 × at 00:49:39, `B-5xx.log`): when the aborted round's orphaned k6 died,
   in-flight `/search` and `/c/{slug}` requests were cancelled and B logged `500` with Postgres "canceling statement due to user
   request". Outside every measured window; cosmetic (499 would be the usual code), but it inflates 5xx dashboards.
4. **A's standing cost.** Whole-namespace idle 0.125 cores vs B 0.021 (demo) — RabbitMQ (≈ 0.06–0.14 idle core, 91–102 MiB),
   10 outbox-relay loops (5 per replica, every 5 s: 45 polls / 20 s in the live SQL count) and the idle SPA shell server
   (59 MiB). At demo load this is 1.4 of the 9.2 ms namespace CPU per journey (total − marginal).
5. **Search dominates B's tails and its DB cost** (J6 p95 149 ms demo, 1.5 s stress; `SearchPostsPaged` bitmap-ORs three
   trigram GINs and rechecks ILIKE on every body — v1 §5.6) — a direct consequence of **D4** (every word matches ≈ 1 300
   threads); the selective word bank is the single most important v2 change for B's numbers.
6. **Static/host/provenance:** `docker compose start` also started the stopped compose `api` container (image built 2026-07-18,
   pre-A-12) on :8080, so the first Lighthouse-A audit ran against that old API (perf 0.75/0.77; kept as attempt 1); the recorded
   audit was rerun against the working-tree API (0.84/0.81). Lab Lighthouse on this shared box is noisy (±0.1 on the same bundle);
   B's misconfig count rose 1 → 5 only because its bench overlay manifests are now scanned.
7. **Operational:** a Bash-tool background task dies with the operator session (the first round attempt); the relaunched round
   ran detached (`setsid nohup`). Other traps hit tonight: `psql -c "ALTER SYSTEM …; SELECT pg_reload_conf()"` fails (one
   transaction), cloc `--vcs=git` must run from inside the repository, a root-owned Go cache breaks uid-1000 containers.

## 7. Threats to validity

- **Single shared host** (cluster + k6 + monitoring in one WSL2 VM; VS Code and other sessions resident; ≈ 1.5 GiB swap in
  use throughout, flat). Mitigated by sequential runs, idle side scaled to 0, ABBA order, reseed + fresh pods, k6 CPU/RSS
  recorded (≤ 1.27 cores, node busy ≤ 4.3 of 8). Capacity A's k6 RSS reached 2.3 GiB with 300 VUs.
- **N = 5 (demo) / N = 1 (stress, capacity).** Only the demo tables carry CIs; "overlap/distinct" is a conservative heuristic,
  not a test. Several H1 differences (app CPU, whole-namespace CPU) are NOT separable at N = 5. Hypothesis-tested tables need
  N ≥ 10 and Welch / Mann-Whitney on the per-repeat values (`round-summary.json` has them).
- **Deviations D1–D11** bias specific numbers in known directions: D1 lowers A's server cost; D2/D11 raise B's bytes and J1/J6
  work; D3 favours B (anonymous reads); D4 makes J6 a worst case on both and drives B's DB cost; D8 shapes the memory numbers;
  D9/D10 are A's own per-request overheads (counted, not removed).
- **The knee is a DB knee.** H2 numbers describe the 1-CPU Postgres profile both sides share; a different DB sizing moves both
  ceilings.
- **One image digest per side, one night.** Everything is reproducible from the archived patches + digests, but it is one
  campaign on one machine.

## 8. Recommendations — the v2 list

1. Repeat demo with N ≥ 10 per side (one more night) and compute Welch / Mann-Whitney on the per-repeat values; repeat capacity
   N ≥ 3.
2. **D4:** shared selective (~300-word Zipf) word bank in BOTH seeders — the largest single distortion of B's DB cost and tails.
3. **D3:** logged-in browsing variant (B's signed-in page = 36 statements; A's = +`has_permission` per private category).
4. **D1:** replay the real SPA waterfall (recorded with a browser) + cache model, or a Lighthouse/RUM run against the cluster on
   the Benchmark dataset.
5. **D2/D11:** decide feed-card previews on B (product decision) or report as is; **D8:** one GC-sensitivity run per side.
6. B: measure J8 receipt latency and loss explicitly; review the slow-consumer policy of the hub under load; return 499 (not
   500) for cancelled requests; Hubert's optional hot-path items (member_count COUNT, SumPostVotes) if the product allows.
7. A: upgrade the frontend toolchain (next 15.5.24+/16.3.3+, sharp 0.35.5, nanoid, postcss, source-map-js) — 13 fixable
   HIGH/CRITICAL CVEs; consider hiding private categories from the anonymous category list; read `effective_perm_cache` in
   `has_permission` (D10) once the scope semantics are settled.
8. J4 (+upload) and J9 in the B adapter; HPA classes (1–3 @ 70 %) on both; dev-loop benchmark with B's toolchain available.
