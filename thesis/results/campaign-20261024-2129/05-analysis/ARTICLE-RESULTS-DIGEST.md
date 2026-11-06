# A/B results digest — campaign v3 (2026-10-24/25), citation-ready

Audience: the article authors (Hubert Ożarowski, Jakub Patkowski). Every number below is copied from an archived file named
next to it; nothing is typed by hand from memory. Full analysis: `05-analysis/CAMPAIGN-REPORT.md`; methodology and the
deviation register D1–D17: `thesis/results/AB-COMPARISON-v1.md` (§5.9 harness h6, §6.2 register, v3 results section).

## What was measured

| item | value | source |
|---|---|---|
| systems | A = React SPA (Next.js, CSR) + .NET 10 modular monolith (JSON API); B = Go SSR monolith (templ + HTMX) | AB-COMPARISON-v1 §1 |
| versions | A tag `thesis-v3-a` = `64cd5e2`, images `forum-dotnet-api/web:git-64cd5e2` (sha256 `2f1cde21…` / `3056a462…`); B tag `thesis-v3-b` = `46b342c`, image `gomx:bench-46b342c` (sha256 `345b7cba…`) | `00-preflight/images-and-provenance.txt` |
| environment | one minikube cluster (8 vCPU / 10 GiB VM, WSL2 host), both apps 2 fixed replicas at 1 CPU / 512 MiB, PostgreSQL 18.4 at 1 CPU / 1 GiB, pool 30, gzip at the shared ingress, TLS, tracing off, limiters neutralized (38/38 profile checks) | `00-preflight/ab_check_profile.txt` |
| dataset | identical Benchmark seeds: 800 users, 1 600 threads, 9 000 comments, 15 000 reactions, 2 000 DMs, 2 400 + 80 friendships; reseeded + fresh pods before every run | `00-preflight/seed-counts.txt` |
| workload | one k6 harness (h6, mix v2): feed, thread, comment, thread + photo, reaction, search (300-word Zipf bank), profile, DM, WebSocket hold with receipt probes | AB-COMPARISON-v1 §5.7–§5.9 |
| design | closed model `demo` (0→10→40→80 VUs, 7 min), **N = 10 runs per side**, ABBA order | round `thesis/results/rounds/20261024-2150-v3/` |
| statistics | exact two-sided Mann-Whitney U + Holm (18 headline metrics), Welch t cross-check, Cliff's δ | `REPORT.md` §Statistical tests |

## Table 1 — H1: resources per served user journey (demo, N = 10 per side)

Source: `thesis/results/rounds/20261024-2150-v3/REPORT.md` (copy: `03-round/REPORT-20261024-2150-v3.md`). Mean ± 95 % CI.
Verdict: MW Holm p; δ = Cliff's delta (negative = B smaller).

| metric (unit) | A | B | B/A | MW Holm p | δ |
|---|---|---|---|---|---|
| application CPU per journey (ms) | 5.824 ± 0.242 | 4.995 ± 0.242 | 0.86 | 0.004 | −0.88 |
| application marginal CPU per journey (ms, idle cost removed) | 5.648 ± 0.273 | 4.901 ± 0.248 | 0.87 | 0.004 | −0.88 |
| database CPU per journey (ms) | 4.733 ± 0.336 | 3.895 ± 0.276 | 0.82 | 0.007 | −0.84 |
| message bus CPU per journey (ms) — A RabbitMQ, B Redis | 2.326 ± 0.208 | 0.219 ± 0.020 | 0.09 | < 0.001 | −1.00 |
| whole namespace CPU per journey (ms) | 13.464 ± 0.923 | 9.728 ± 0.548 | 0.72 | < 0.001 | −1.00 |
| whole namespace marginal CPU per journey (ms) | 11.779 ± 0.933 | 9.260 ± 0.642 | 0.79 | < 0.001 | −0.98 |
| whole namespace idle cores (no traffic) | 0.121 ± 0.044 | 0.035 ± 0.021 | 0.29 | 0.020 | −0.76 |
| application memory, avg (MiB, 2 replicas) | 412 ± 6 | 73 ± 4 | 0.18 | < 0.001 | −1.00 |
| whole namespace memory, avg (MiB) | 1 268 ± 55 | 793 ± 36 | 0.63 | < 0.001 | −1.00 |
| shared ingress CPU per journey (ms) | 2.131 ± 0.272 | 2.677 ± 0.124 | 1.26 | 0.012 | +0.80 |
| data received per journey (KiB, gzip + TLS) | 3.273 | 10.241 | 3.13 | < 0.001 | +1.00 |
| HTTP requests per run | 62 645 ± 2 788 | 36 610 ± 1 055 | 0.58 | — | — |

## Table 2 — realtime (WebSocket receipt probes, demo, N = 10)

| metric (unit) | A | B | MW Holm p |
|---|---|---|---|
| probes sent / received per run | 220 / 220 | 220 / 220 | — |
| probe loss (%) | 0.00 | 0.00 | 1.000 |
| receipt latency median (ms) | 2 344 ± 519 | 40 ± 7 | — |
| receipt latency p95 (ms) | 5 816 ± 1 452 | 403 ± 165 | < 0.001 |

Source: REPORT.md "WebSocket holds (J8)". A pushes through a transactional outbox polled every 5 s (at-least-once); B publishes
to Redis inside the request (deviation D14; B's figure is bounded below by its own POST time).

## Key findings (one sentence each)

1. **Application tier:** at equal closed-model load B spends 14 % less CPU per served journey than A (4.995 ± 0.242 vs
   5.824 ± 0.242 ms; MW Holm p = 0.004; δ = −0.88; N = 10 per side) — with A's client idealized (D1: A's cost is a lower bound).
2. **Database tier:** B spends 18 % less Postgres CPU per journey (3.895 ± 0.276 vs 4.733 ± 0.336 ms; p = 0.007) — the
   opposite of v1 (B 1.69× A), after both seeds got the selective 300-word search bank (D4) and equal 300-char card excerpts (D2).
3. **Whole deployment:** B's namespace costs 28 % less CPU per journey (9.73 vs 13.46 ms; p < 0.001), 21 % less after
   removing idle cost; the single largest gap is the message bus (A RabbitMQ 2.33 vs B Redis 0.22 ms per journey).
4. **Standing cost:** with no traffic A's namespace burns 0.121 cores vs B's 0.035 (p = 0.020) — outbox relays, RabbitMQ
   and the SPA shell server.
5. **Memory:** A's application uses 5.6× B's memory (412 vs 73 MiB; namespace 1 268 vs 793 MiB; p < 0.001) under each
   platform's default GC policy (.NET Server GC vs Go GOGC = 100, deviation D8).
6. **Transfer:** B sends 3.1× more bytes per journey (10.24 vs 3.27 KiB) even with equal card text — the remainder is HTML
   markup (≈ 6.4 KB per feed card) against JSON (≈ 0.7 KB per card); v1's gap was 5.25× with full bodies in B's cards.
7. **Realtime:** B delivers a comment's live update in 40 ms median (p95 403 ms), A in 2.3 s (p95 5.8 s), both without loss —
   the price of A's transactional outbox polled every 5 s.
8. **Method (must accompany any latency or throughput claim):** the shared-host load generator used up to 5.5 of 8 cores
   because every thread-with-photo journey synthesizes a 256×256 PNG in k6's JavaScript VM (≈ 0.7 core-s per image), so the
   v3 latencies are those of a saturated host and the v3 capacity runs collapsed at 100–150 journeys/s on both sides — H2 is
   not measured in v3 (v1's capacity result, A 300 vs B 200 journeys/s sustained, remains the only H2 data).

## Caveats that must travel with the numbers

- **Latency and throughput (H1 latency, H2) are not usable from v3** — host contention (finding 8). Resource-per-journey
  metrics come from cgroup CPU accounting (work done) and are robust; contention may still inflate them modestly on both sides.
- **D1:** A's harness client is idealized (fewer calls than the real SPA) → A's server cost is a lower bound.
- **D3:** read journeys are anonymous (favours B: a signed-in B page adds session and per-card queries).
- **D8:** memory reflects platform-default GC policies.
- **D9/D10:** A's per-request overhead (Npgsql reset, per-endpoint read gate, SQL ACL) is counted as A's design cost.
- **D12/D14:** B's photo journey waits ≥ 1 s for its upload poll; A's realtime floor is its outbox poll.
- **D15/D16:** photo and DM journeys mirror B's placement/peer rules; per-send work differs by design.
- **D17 (new):** ≈ 6 % of A's DM journeys failed with 404 because A's seed links some benchmark users to blocked accounts
  (A-only); this lowers A's DM write work slightly (A failed checks 0.12 % of requests, B 0 %).
- One host, one night, one image digest per side; demo only (no stress/HPA/auth/real-SPA variants in v3).

## Figures (`thesis/figures/v3/`, PNG 200 dpi + SVG; rendered from the round archive)

| file | caption (PL) | note |
|---|---|---|
| `cpu-per-journey-by-layer-demo.png` | Czas CPU zużyty na jedną obsłużoną podróż [ms] według warstwy (aplikacja, baza, szyna, ingress, cała przestrzeń nazw całkowicie i marginalnie); profil demo, średnia z 10 powtórzeń, słupki błędu = 95 % CI. | primary H1 figure |
| `memory-by-layer-demo.png` | Średnie zużycie pamięci [MiB] w oknie scenariusza demo według warstwy; A z domyślnym Server GC .NET, B z domyślnym GOGC=100 (D8). | |
| `per-repeat-values-demo.png` | Wartości z pojedynczych powtórzeń (kropki) i średnie (kreski) dla pięciu metryk z wartościami p (Welch, Mann-Whitney) i deltą Cliffa. | in v3 ALL five metrics separate, incl. application CPU — the generated caption's last sentence ("CPU aplikacji się przeplata") is v1 text and must be dropped |
| `latency-per-journey-demo.png` | Opóźnienie podróży (mediana, p95) w profilu demo, N = 10. | **only with the host-contention caveat** — not an H1 latency result |

Figures were rendered with a scratch copy of `scripts/thesis_figures.py` whose only change is the sample size in three titles
(the tracked script hard-codes "N = 5"; patch: `05-analysis/thesis_figures-N-label-fix.patch`).

## Table 3 — functional gates on the measured tags (fresh clones, 2026-10-25)

Source: `01-functional/functional-summary.json` (TRX files in `01-functional/A/trx/`, `go test -json` in `01-functional/B/`).

| gate | A (`thesis-v3-a`) | B (`thesis-v3-b`) |
|---|---|---|
| unit + integration tests | 259 passed / 0 failed / 0 skipped (48 integration on Testcontainers) | `go test -race`: 388 / 0 / 25 skipped (service tests, run in the lanes below); Postgres lane 81 / 0 / 0; Redis lane 1 / 0 / 0 |
| static checks | build 0 errors (32 NU1903 warnings, test projects only), `dotnet format` clean | vet, gofmt, templ fmt, generated code, golangci-lint 1.64.5 clean |
| frontend | typecheck, lint, vitest 71 / 71, production build | lint:ts (eslint, tsc, prettier) clean |

## Table 4 — static metrics (H3)

Source: `04-static/static-summary.json`, `04-static/STATIC-SUMMARY.md` (trivy 0.72.0, cloc 2.10).

| metric (unit) | A | B |
|---|---|---|
| fixable HIGH/CRITICAL vulnerabilities, repository scan (count) | 0 (was 13 on 2026-10-18) | 0 |
| fixable HIGH/CRITICAL vulnerabilities, deployed images (count) | 0 (api), 0 (web) | 0 |
| HIGH misconfigurations in manifests/Dockerfiles (count) | 5 | 5 |
| core application code (lines, cloc) | 53 914 | 30 575 |
| container images (MB) | 178.9 (api) + 248.8 (web) | 41.2 |
| first-load web assets (KB, gzip) | 211.9 | 145.3 |
| direct dependencies | 42 NuGet versions; 9 + 15 npm | 29 Go modules (+ 105 indirect); 9 + 27 bun |

## Table 5 — Lighthouse on the cluster (benchmark data, median of 3 runs per page)

Source: `04-static/lighthouse/lighthouse-metrics-cluster-{a,b}-{mobile,desktop}.txt`; comparison file
`04-static/lighthouse/comparison-v3-vs-20261018.md`. perf = Performance score [0–1]; LCP/TBT/FCP in ms; CLS unitless.

| preset | page | metric | A v3 | B v3 | A 2026-10-18 | B 2026-10-18 |
|---|---|---|---|---|---|---|
| mobile | feed | perf | 0.72 | 0.89 | 0.56 | 0.89 |
| mobile | feed | LCP ms | 2794 | 2178 | 2834 | 2221 |
| mobile | feed | TBT ms | 698 | 11 | 776 | 34 |
| mobile | feed | CLS | 0.149 | 0.195 | 0.237 | 0.195 |
| mobile | feed | FCP ms | 917 | 1356 | 998 | 1353 |
| mobile | feed | a11y | 0.91 | 0.88 | 0.91 | 0.88 |
| mobile | thread | perf | 0.88 | 0.94 | 0.85 | 0.94 |
| mobile | thread | LCP ms | 2896 | 2017 | 2742 | 2028 |
| mobile | thread | TBT ms | 336 | 7 | 442 | 12 |
| mobile | thread | CLS | 0.001 | 0.139 | 0.000 | 0.139 |
| mobile | thread | FCP ms | 916 | 1203 | 909 | 1203 |
| mobile | thread | a11y | 0.95 | 0.94 | 0.95 | 0.94 |
| desktop | feed | perf | 0.97 | 1.00 | 0.97 | 1.00 |
| desktop | feed | LCP ms | 715 | 484 | 597 | 482 |
| desktop | feed | TBT ms | 97 | 0 | 109 | 0 |
| desktop | feed | CLS | 0.090 | 0.012 | 0.078 | 0.018 |
| desktop | feed | FCP ms | 264 | 323 | 248 | 322 |
| desktop | feed | a11y | 0.95 | 0.89 | 0.95 | 0.89 |
| desktop | thread | perf | 1.00 | 1.00 | 0.99 | 1.00 |
| desktop | thread | LCP ms | 652 | 443 | 651 | 442 |
| desktop | thread | TBT ms | 14 | 0 | 68 | 0 |
| desktop | thread | CLS | 0.001 | 0.001 | 0.001 | 0.001 |
| desktop | thread | FCP ms | 255 | 282 | 248 | 282 |
| desktop | thread | a11y | 1.00 | 0.94 | 1.00 | 0.94 |

Caveat: the 2026-10-18 columns come from a tooling-validation run under CPU contention (A's older web image) — use the v3
columns as the result; the v3 runs were one quiet-host pass (3 runs per page), not repeated rounds.

Figure: `thesis/figures/v3/lighthouse-cluster.png` — Lighthouse Performance score and Total Blocking Time for the feed and
thread pages, mobile (slow-4G, 4× CPU slowdown) and desktop, median of three runs (the generated caption's "validation run"
remark refers to 2026-10-18; v3 ran on a quiet host).
