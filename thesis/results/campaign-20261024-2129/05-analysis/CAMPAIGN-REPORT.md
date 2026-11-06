# A/B test campaign v3 report — forum-dotnet (A) vs gomx (B), night of 2026-10-24/25

> Campaign folder: `thesis/results/campaign-20261024-2129/` (this file: `05-analysis/CAMPAIGN-REPORT.md`; step log:
> `PROGRESS.md`; artifact index: `index.json`; citation-ready digest: `05-analysis/ARTICLE-RESULTS-DIGEST.md`). Every number
> below is copied from an archived file named next to it — primarily `thesis/results/rounds/20261024-2150-v3/REPORT.md`
> (copied to `03-round/`). Methodology, hypotheses, fairness invariants F1–F8 and the deviation register D1–D16 / D-x:
> `thesis/results/AB-COMPARISON-v1.md` (§5.8 = h5/v2 changes, §5.9 = h6 audit fixes, §6.2 register; the v3 tables are in its
> new results section). Statistics (h6): mean ± 95 % CI (Student t) and median [p25–p75] per side over the valid repeats;
> the verdicts are exact two-sided Mann-Whitney U with Holm adjustment over each metric family (primary), Welch's t + Holm
> as the parametric cross-check, Cliff's δ as the effect size. Ratios are B/A.

## 0. Version under test (Phase 0 — `00-preflight/`)

| | A (React SPA + .NET 10 modular monolith) | B (Go + templ/HTMX SSR monolith) |
|---|---|---|
| repo / branch | `~/projects/forum-dotnet`, `31-feat-phase-13---ab-benchmark` | `~/projects/gomx`, `feature/master-thesis` |
| tag = commit | **`thesis-v3-a` = `64cd5e2`** (committed 2026-10-24 20:09 CEST), working tree clean | **`thesis-v3-b` = `46b342c`**, working tree clean |
| deployed image | `forum-dotnet-api:git-64cd5e2` sha256 `2f1cde21…`, `forum-dotnet-web:git-64cd5e2` sha256 `3056a462…` | `gomx:bench` = `gomx:bench-46b342c` sha256 `345b7cba…`; `/stats` version `46b342c` |
| clean-build proof | `rule4-clean-build-proof.txt`: porcelain outside `thesis/results` empty, `git diff --quiet HEAD` = 0, `describe --exact-match` = `thesis-v3-a`. `meta-A.json` reads `git_dirty: true` only because the round's own archive is untracked (known artefact, true for every round) | same checks; B's tree was never written to |
| DB schema | 16 EF migrations, last `20261024001000_AddFeedExcerpt`, `20261024002000_OptimizeEffectiveMask`; `thread_excerpt`, `effective_mask`, `has_permission` present | goose migrations at boot (fresh DB) |
| Postgres | `PostgreSQL 18.4 (Debian 18.4-1.pgdg13+1)`, image `postgres:18` digest `3a82e1f5…` | identical |
| harness | `load/k6` mix `v2`, harness **`h6-2026-10-24`**, k6 v2.1.0 | same harness, adapter `targets/b.js` |
| common profile | **38/38 checks ok** (`ab_check_profile.txt`): 2 fixed replicas, no HPA, 250m/256Mi → 1 CPU/512Mi, probe timeouts 5 s, tracing off, limiters neutralized (A 1 000 000 permits; B `GOMX_AUTH_RATELIMIT=0`), pool 30 (A Npgsql, B `GOMX_DB_MAX_CONNS=30`), B `GOMX_CARD_PREVIEW_CHARS=300`, `postgres:18` with the same args/resources (1 CPU / 1 GiB) | |
| dataset | Benchmark seed 800 users / 1 600 threads / 9 000 comments / 15 000 reactions / 2 000 DMs (+ 2 400 + 80 friendships, 800 conversations) — reseeded before every run | identical counts (`seed-counts.txt`) |

Provenance notes: both deploy scripts build INSIDE minikube's docker daemon, so the host-docker images built at ~20:25 CEST
were not used (the prompt assumed they would be). The API image's `created` field (20:22 CEST) predates tonight's deploy
because an earlier, terminated `make mk-deploy` of the same clean commit left its BuildKit layers in minikube's cache;
tonight's build reported every step `CACHED` and wrote the same content-addressed image (note in
`images-and-provenance.txt`). The B tag could not be checked on GitLab from this shell (no credentials); the commit is on
the remote (branch tracks origin, 0 ahead).

## 1. Functional tests (Phase 4.1 — `01-functional/`, `functional-summary.json`)

Run after the round on **fresh `git clone --branch <tag>` copies** in the operator scratchpad (the measured trees were never
touched), 02:15–02:52 CET.

| gate | A (`thesis-v3-a`) | B (`thesis-v3-b`) |
|---|---|---|
| build / static checks | `dotnet build Forum.slnx`: 0 errors, **32 warnings — all NU1903** (`SSH.NET 2024.1.0`, two HIGH advisories published after 2026-10-18; transitive via Testcontainers in the 8 test projects only — not in any runtime image) | `go vet` clean; golangci-lint v1.64.5 clean (first attempt failed on a `proxy.golang.org` TLS timeout while the host was overloaded — rerun clean, both logs kept); generated code up to date; gofmt + `templ fmt` clean |
| tests | **259 passed / 0 failed / 0 skipped** (8 projects; 48 integration tests on Testcontainers Postgres 18 + MinIO + RabbitMQ) — TRX in `A/trx/` | `go test -race ./...`: **388 passed / 0 failed / 25 skipped** (the 25 = adapter tests that need DATABASE_URL/REDIS_ADDR — skipped in the CI fast lane too); coverage 30.8 % |
| service lanes | (inside the integration suite) | Postgres lane **81 / 0 / 0** (72.6 %); Redis lane **1 / 0 / 0** |
| formatting / lint | `dotnet format --verify-no-changes` clean | `lint:ts` (bun 1.3.14): install, generated-TS check, eslint, `tsc --noEmit`, prettier — clean |
| frontend | `npm ci`, `tsc --noEmit`, `next lint` clean; vitest **71 / 71**; `next build` OK | (SSR; assets in the image) |
| verdict | **PASS** | **PASS** |

`functional-summary.json` holds the per-project counts and step timings. Environment note: the first B attempt ran in
parallel with A's suites and a cluster reseed and pushed the host into swap thrashing (load ≈ 65); it was stopped and re-run
alone (`B-attempt1-aborted/` kept). While `go test -race` ran, the cluster's exec probes failed again (A RabbitMQ restarts
16 → 25, B Redis 15 → 33) — no measurement was active. Not replayable here: B's `e2e:playwright`/`lighthouse` CI jobs (prebaked
registry image), govulncheck, Sonar.

## 2. Live checks through the ingress (Phase 1 — `02-live-checks/SUMMARY.md`)

On the fresh Benchmark seeds, before the gate: every h6 endpoint answers as the adapters expect on both sides, incl. the
journeys new since v1 — A J4 = initiate 201 → presigned PUT 200 to `https://minio.forum.local` → commit 200 → untagged
thread 201 → attach 204; B J4 = `POST /uploads` 202 → status `completed` after 1 poll → `POST /posts` 303 `/thread/1601`;
A J9 = friends (6) → open 200 → send 200; B J9 = 303 `/messages/28` without `?error=rejected`; J8 = ticket → 101 on A,
cookie → 101 on B. TLS 1.3 + HTTP/2 + gzip at the shared ingress on both; health 200 on both; Prometheus 19/19 targets up.

- **Posture.** A: anonymous category list 0 private / 8; private thread / feed / comments / category-by-slug → 404;
  0 private rows in anonymous search. B: `/auth/google` → 303 `/login?error=oauth_unavailable` without a session cookie;
  private community / post → 404; `/healthz`, `/readyz`, `/stats` reachable through the ingress (as in v1).
- **B private-comment WebSocket gate (thesis-v3-b) — PASS live** (`b-ws-private-gate/`): a member's comment on a private
  post reached the member socket (1 frame) and neither the anonymous nor the non-member socket (0 / 0); a public comment
  reached all three. The v1-era leak is closed in the measured build.
- **A ticket is single-use per replica** (documented Phase 7 design, newly visible): one ticket, four handshakes →
  `101 101 401 401` with 2 replicas (repeated: identical). v1's single replay hit the same replica. Exposure bound: one socket
  per replica within the 30-s ticket TTL.
- **Card sizes (the D2/D11 question).** Card text is now equal (A 20/20 excerpts of exactly 300 chars; B 300-char
  previews). Feed page 1 gzip: A 3.8 KB + 0.5 KB batch vs B 15.4 KB (v1: 1.6 + 0.5 vs 16.2 KB) → gzip B/A **7.9× → 3.6×**;
  search page 1: **8.2× → 3.8×**. The byte gap halved but remains — B's card markup is ≈ 6.4 KB raw per card (vote controls,
  HTMX attributes, author block, icons) against ≈ 0.7 KB JSON per card on A: the HTML-vs-JSON paradigm (D-1), not content.
- **Search word bank live in both seeds** (`00-preflight/search-bank.txt`, `search-bank-full.tsv`): rank 1 `abandon` 1 600 /
  1 600, rank 50 `beacon` 614 / 613, rank 100 `cousin` 331 / 295 (B/A 0.891 — 0.9 pp outside the ±10 % check; identical to
  the 2026-10-18 counts, so deterministic: two PRNGs draw from the same Zipf bank), rank 200 `midnight` 146 / 141, rank 300
  `zephyr` 97 / 96. Over all 300 words the anonymous J6 result sets total 90 656 (A) vs 91 371 (B) rows (B/A 1.008; 302 vs
  305 per query); per word |B/A − 1| median 4.9 %, max ≈ 40 % (e.g. `peacock` 143 / 92, `hamlet` 174 / 225).
- **Environmental incident before the round** (logged, no effect on measurements): during B's image build on the shared node
  (Go compile + bun install) A's RabbitMQ failed its exec liveness probe (`rabbitmq-diagnostics`, 10 s) and restarted at
  21:25, A's Postgres probes timed out once, and one A backend pod reported broker loss and `/health/ready` 503 for ~1 min.
  The round builds nothing and restarts A's app pods before every run; RabbitMQ's restart counter is part of each run's
  restart check.

## 2b. SQL statements per journey (Phase 4.2 — `02-live-checks/sql-per-journey/`, pristine reseed, after the round)

Method of the 2026-10-18 campaign: `log_statement=all` on both Postgres pods for one window (reset and verified `none`
afterwards — `run.log`), two warm-up passes, then psql markers around ONE request set per journey; statements classified
offline by `sql-classify.py` (joins multi-line statements, files every statement of a monitoring/psql connection by PID,
matches B's River polls by their sqlc names; River enqueue/completion caused by J4 counts as journey work). Raw logs:
`A-postgres.log`, `B-postgres.log`; result: `summary-classified.{txt,json}` (the first-pass `summary.txt` misclassified
multi-line exporter queries and River polls and is kept only for transparency).

| journey | A foreground statements (requests) | B foreground statements (1 request) | change vs v1 (AB-COMPARISON-v1 §5.6) |
|---|---|---|---|
| nav | 1 (1) | 1 | = |
| J1 page 1 | 4 (2): read gate, feed **+ `thread_excerpt()` per row**, batch ×2 | 2: community, posts | = counts; A's feed statement now builds 20 excerpts |
| J2 thread (heaviest / typical) | 5 (3): read gate, detail, tree, batch ×2 | 3: post, community, comments | = |
| J6 search | 1 (1) **+ excerpts** | 1 | = counts (B's search is selective now — D4 closed) |
| J7 profile | 2 (2) **+ excerpts on the threads list** | 3 | = |
| J3 comment | 5: thread, `has_permission`, INSERT, EF savepoint pair (+ tx/outbox in the background) | **6**: session, post, community, INSERT, **post + community for the private-comment WebSocket gate** | B +2 (thesis-v3-b gate) |
| J5 reaction add | 5: target ⋈ category, `has_permission`, existing reaction, INSERT, summary | **8**: session, **post, community (visibility gate)**, `FOR UPDATE`, dir, upsert, `SumPostVotes`, dir | B +2 (thesis-v3-b gate) |
| J5 reaction remove | 9 (incl. 2 savepoint pairs) | 8 | |
| J4 thread + photo | 10 over 5 requests: file insert, file read, commit update, category, `has_permission`, thread insert, file read, thread read, attachment count, attachment insert | 15: session ×4, media upload create/read/update, **River job insert + NOTIFY + completion**, community ×2, membership, post insert | new in mix v2 |
| J9 DM (first send to a pair) | 14 over 2 requests: active-user check, block check, privacy, conversation lookup, conversation + 2 participants insert, seat reads, block + privacy re-check, read-marker update, message insert | 6: session, `AreFriends`, user, insert, unread count, unread list | new; A's later sends skip the open (≈ 7) |
| idle 20 s, 2 replicas | 76 background: 18 Npgsql resets, 10 health `SELECT 1`, 48 postgres-exporter | 172 background: 82 River polls/maintenance, 62 pgx pings, 28 tx | v1: A 188 / B 116 |

Reading: the read paths execute the same NUMBER of statements as in v1 on both sides; A's feed/search/profile statements
are heavier because they compute the 300-char excerpt for the page rows (D2 parity), and B's write paths gained the two
v3 gates (+2 statements on J3 and J5). **A's background counts in this window are NOT representative:** at 01:29:44 UTC all
five outbox relays logged `Outbox relay for '<module>' failed; retrying in 00:00:10` and the consumer hosts/realtime hub lost
the broker — A's RabbitMQ was restarting under the host overload caused by the parallel Phase 4.1 test suites (A RabbitMQ
restart counter 16 → 25 during Phase 4). The idle window (01:29:59–01:30:19 UTC) therefore caught every relay in backoff (0
claims; v1 counted 45 per 20 s). Foreground counts are unaffected (A's writes land in the outbox table and do not need the
broker synchronously). The v1 idle figures (A 188 / B 116 statements per 20 s) remain the reference for standing DB chatter. Per-request work therefore
explains part of the v1 → v3 DB shift: B's J6 went from a 1 300-row ILIKE scan to a selective one, A's reads gained the
excerpt function and the mix added J4/J9 writes (A's J9 first send = 14 statements).

## 3. The measured round (Phase 3 — archive `thesis/results/rounds/20261024-2150-v3/`, copies in `03-round/`)

Gate `20261024-2137-v3-gate` (smoke:1): **2/2 valid**; write checks A 68/68 comments · 21/21 threads · 21/21 DMs, B 45/45 ·
21/21 · 23/23; failed checks A 0.17 % (2 × `dm_open` 404 — finding D17 below), B 0.00 %; core p95 22 vs 22 ms; J8 probes 8/8
on both, receipt median 1 243 vs 6 ms (D14); k6 ≤ 47 % of one core.

Round `20261024-2150-v3`: plan `demo:10,capacity:1` (22 runs, ABBA, reseed + fresh pods + 60-s warm-up with ~6 % writes
before every run), 21:50 → 02:33 CEST, **18/22 valid**. Resume `--plan demo:10` (02:46 CEST → 02:11 CET; the clocks went
back at 03:00 CEST) redid the two invalid demo runs → **demo 10 + 10 valid**. Capacity A and B are INVALID (pod restarts) and
were deliberately not redone (§3.3). Invalid attempts stay in the archive under `invalid-attempts/` (demo B-5: B Redis
restart; demo A-10: A RabbitMQ restart). Every valid run: 0 × 429, 0 pod restarts, write checks exact (accepted writes =
new rows for comments, threads and DMs in every run), failed checks A 0.11–0.15 % (= the D17 `dm_open` 404s, §6), B 0.00 %,
common-profile check passed before the run, `resources.json` present.

### 3.0 Read first: the load generator saturated the shared host

| | v3 demo (this round) | v1 demo (2026-10-18) |
|---|---|---|
| k6 CPU, max (% of one core; REPORT "Load generator") | A 529 % / B 549 % | A 44 % / B 74 % |
| k6 CPU p90 / median (from `samples-*.json`) | ≈ 3.8–4.5 cores / ≈ 1.5–1.8 cores | < 1 core |
| node busy cores avg (8 vCPU shared by k6 + cluster + monitoring) | A 5.01 ± 0.21 / B 4.81 ± 0.20 | A 2.97 ± 0.12 / B 2.93 ± 0.15 |
| host load average (15 min) during the round | up to ≈ 14 | — |

**Cause (measured):** J4 synthesizes a fresh 256×256 RGBA PNG per journey inside k6's JavaScript VM (`makePng` in
`load/k6/lib/assets.js`, called inside the timed journey on both adapters). Micro-benchmark on the idle host after the round
(`05-analysis/k6-makepng-bench/`): **603 ms and ≈ 0.7 core-seconds per 256-px image** (incl. Go GC on other cores);
128 px 121 ms, 64 px 32 ms, 32 px 8 ms. At the demo plateau (≈ 150 journeys/s × 3 % J4 ≈ 4.5 images/s) that is ≈ 3.2 cores of
k6 CPU on a host whose 8 vCPUs also run both namespaces' pods. J4 had weight 0 in v1, which is why v1 never saw it; the h6
gate (5 VUs) used ≤ 0.47 core and could not reveal it.

**Consequences.** (i) Closed-model latencies and journeys/s are contaminated by CPU contention on BOTH sides: core p95
386 vs 274 ms (v1: 19 vs 61 ms), journeys/s 72.6 vs 74.4 (v1: 91.6 vs 90.1) — these are latencies of a saturated machine, not
H1 latencies at moderate load, and they are not used for H1 conclusions. (ii) Exec probes of infrastructure pods time out at
peak node load: B Redis (`redis-cli ping`, 1 s) restarted twice in the measured window (once during an A run — not counted —
and once in B demo run 5), A RabbitMQ (`rabbitmq-diagnostics status`, 15 s) once in A demo run 10, and in the capacity runs A
Postgres + RabbitMQ and B Redis. (iii) H2 is not measurable with this harness on this host (§3.3).
**What stays valid:** CPU core-seconds per journey from cgroup accounting (work done, not time waited), memory, bytes per
journey, semantic/write checks, WebSocket correctness (probe loss 0 %), SQL statements per request (§2b). Caveat: contention
may inflate CPU per journey modestly on both sides (cache/context-switch effects); the A/B comparison runs under the same
contention pattern (same harness, ABBA order).

### 3.1 H1 — demo (closed model 0→10→40→80 VUs over 7 min, 20 WS VUs; N = 10 per side)

Resources over the scenario window (`03-round/REPORT-20261024-2150-v3.md`; mean ± 95 % CI, median [IQR]; verdict = exact
Mann-Whitney U with Holm over the 18 headline metrics; δ = Cliff's delta, negative = B smaller):

| metric | A | B | B/A | MW Holm p | δ | verdict |
|---|---|---|---|---|---|---|
| app CPU per journey (ms) | 5.824 ± 0.242 · 5.866 [5.627–5.980] | 4.995 ± 0.242 · 5.178 [4.716–5.239] | 0.86× | 0.004 | −0.88 | distinct |
| app MARGINAL CPU per journey (ms) | 5.648 ± 0.273 | 4.901 ± 0.248 | 0.87× | 0.004 | −0.88 | distinct |
| DB CPU per journey (ms) | 4.733 ± 0.336 | 3.895 ± 0.276 | 0.82× | 0.007 | −0.84 | distinct |
| bus CPU per journey (ms) — A RabbitMQ / B Redis | 2.326 ± 0.208 | 0.219 ± 0.020 | 0.09× | < 0.001 | −1.00 | distinct |
| WHOLE namespace CPU per journey (ms) | 13.464 ± 0.923 | 9.728 ± 0.548 | 0.72× | < 0.001 | −1.00 | distinct |
| WHOLE namespace MARGINAL CPU per journey (ms) | 11.779 ± 0.933 | 9.260 ± 0.642 | 0.79× | < 0.001 | −0.98 | distinct |
| WHOLE namespace idle cores | 0.121 ± 0.044 | 0.035 ± 0.021 | 0.29× | 0.020 | −0.76 | distinct |
| app memory avg (MiB, 2 replicas) | 412 ± 6 | 73 ± 4 | 0.18× | < 0.001 | −1.00 | distinct |
| WHOLE namespace memory avg (MiB) | 1 268 ± 55 | 793 ± 36 | 0.63× | < 0.001 | −1.00 | distinct |
| ingress-nginx CPU per journey (ms, shared edge) | 2.131 ± 0.272 | 2.677 ± 0.124 | 1.26× | 0.012 | +0.80 | distinct |
| KiB received per journey (wire, gzip + TLS) | 3.273 | 10.241 | 3.13× | < 0.001 | +1.00 | distinct |

Also (REPORT): app idle cores 0.013 vs 0.007, DB memory 278 vs 297 MiB (overlap), bus memory 156 vs 14 MiB, A frontend pod
45 MiB, HTTP requests per run 62 645 vs 36 610 (an A journey = 1.7 requests on average vs 1 on B).

Latency (contaminated by host contention — §3.0; reported for completeness, verdicts are "raw-only" at the Holm level except
where marked): core median 26.4 vs 19.8 ms, core p95 386 vs 274 ms (MW raw p 0.015, Holm 0.088), HTTP request p95 227 vs
255 ms (no difference). Per journey (Holm-distinct only): J1 p95 414 vs 269 ms, J2 median 24.4 vs 10.1 and p95 345 vs
196 ms, J5 median 32.3 vs 16.4 ms, J9 median 42.2 vs 22.6 ms (B faster); J6 median 23.6 vs 43.5 ms (A faster); J4 median
1 062 vs 2 039 ms (includes ≈ 0.94 s of k6-side PNG synthesis on both sides, §3.2). Under contention the side that issues more
requests per journey and spends more CPU per journey (A) waits longer — this is not the v1 H1 latency comparison.

WebSocket (J8; A per-view subscription, B broadcast): 80 vs 80 subscribed, 27 766 vs 85 613 notifications, 0 errors, 0 early
closes, receipt probes 220/220 on both (loss 0 %); **receipt latency median 2 344 vs 40 ms, p95 5 816 vs 403 ms** (MW Holm
p < 0.001) — A's floor is the 5-s outbox relay poll (D14; B's figure is bounded below by its own POST, D14 caveat).

### 3.2 J4 decomposition (per-endpoint request latencies, demo mean of 10 runs — `05-analysis/per-endpoint-demo.txt`)

| step | A (median / p95 ms) | B (median / p95 ms) |
|---|---|---|
| upload | initiate 19.3 / 411 · presigned PUT to MinIO 26.7 / 873 · commit 32.8 / 464 | `POST /uploads` 67.9 / 502 · status poll 5.9 / 160 (+ 1-s client sleep) |
| create | thread 19.5 / 330 · attach 19.4 / 280 | `POST /posts` 20.5 / 267 |
| sum of request medians | ≈ 118 ms (5 sequential requests) | ≈ 95 ms + 1 000 ms poll wait |
| J4 journey median | 1 062 ms | 2 039 ms |
| remainder = k6-side work (PNG synthesis) | ≈ 944 ms | ≈ 944 ms |

The k6-side remainder is identical on both sides, i.e. the PNG cost is symmetric; the server-side J4 work is the request rows.

### 3.3 H2 — capacity (open model 25…500 j/s, 11 × 60 s) — INVALID on both sides, not redone

Both runs collapsed at 100–150 offered journeys/s (`05-analysis/capacity-k6-vs-sut-cores.txt`):

| step | A offered → achieved, core p95 | A cores: k6 / app / DB | B offered → achieved, core p95 | B cores: k6 / app / DB |
|---|---|---|---|---|
| c01 | 25 → 25.0, 22 ms | 0.70 / 0.22 / 0.12 | 25 → 25.0, 21 ms | 0.59 / 0.12 / 0.04 |
| c02 | 50 → 50.0, 35 ms | 1.41 / 0.36 / 0.24 | 50 → 50.0, 34 ms | 1.57 / 0.23 / 0.11 |
| c03 | 100 → 99.6, 409 ms | **4.04** / 0.53 / 0.48 | 100 → 97.5, 1 142 ms | **3.70** / 0.57 / 0.43 |
| c04 | 150 → 22.4, 60 s, 14 % failed | 1.32 / – / – (restarts) | 150 → 33.9, 29.5 s | 2.35 / 0.10 / 0.00 |
| c05–c11 | collapse, 100 % failed from c07 | | degraded, 44–96 achieved | |

Report lines: A knee c04 @ 150 j/s, sustained 100 j/s, 117 865 dropped iterations; B knee c03 @ 100 j/s, sustained 50 j/s,
119 696 dropped iterations (v1: A sustained 300 / knee 400; B sustained 200 / knee 300). At 100 j/s the load generator used
≈ 4 cores while each SUT used ≈ 1 (app + DB); the restarts (A Postgres + RabbitMQ, B Redis) followed from node overload.
These numbers describe the shared host saturated by k6, not either architecture, and are excluded from every H2 statement.
A redo with the same harness would reproduce the collapse, so the resume skipped capacity (deliberate deviation from the
prompt's "same plan" rule). H2 needs the J4 synthesis fix (or a smaller synthetic photo, §8) first.

## 4. Static metrics (H3) (Phase 4.3 — `04-static/STATIC-SUMMARY.md`, `static-summary.json`)

| metric | A | B | v1 (2026-10-18) A / B |
|---|---|---|---|
| trivy fs: HIGH/CRITICAL vulnerabilities with a fix | **0** | 0 | 13 / 0 |
| trivy image scans (HIGH/CRITICAL with a fix) | api 0, web 0 | 0 | — |
| misconfigurations (HIGH, manifests/Dockerfiles) | 5 | 5 | 5 / 5 |
| NuGet audit (build warnings) | 32 × NU1903 `SSH.NET 2024.1.0` (test projects only, via Testcontainers) | — | 0 / — |
| core code (cloc, lines) | 53 914 (797 files) | 30 575 (298 files) | 52 097 / 30 088 |
| container images | 178.9 MB api + 248.8 MB web | 41.2 MB | 178.8 + 243.5 / 41.2 MB |
| first-load web assets (gzip) | 211 893 B | 145 257 B | 210 702 / 145 250 B |
| dependencies | 42 NuGet versions; 9 + 15 npm, 669 lockfile packages | 29 + 105 Go modules; 9 + 27 bun, ≈ 1 320 lock entries | |

### 4.1 Lighthouse on the cluster (quiet host, benchmark data; `04-static/lighthouse/`)

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

A's mobile feed improved since 2026-10-18 (0.56 → 0.72; TBT 776 → 698 ms; CLS 0.237 → 0.149 — the 2026-10-24 mobile
fixes, but the 2026-10-18 reference was a tooling-validation run under CPU contention, so part of the change is the quieter
host) and stays behind B (0.89; TBT 11 ms); desktop near-equal. Screenshots of both apps: `04-static/screenshots/` (A's home page renders again).

## 5. What the data supports (and does not)

**H1 — resources per served journey at equal closed-model load (demo, N = 10 per side, MW + Holm):**
- *B spends less CPU per journey in every tier that does the forum's work:* application 4.995 vs 5.824 ms (−14 %, p = 0.004,
  δ = −0.88), database 3.895 vs 4.733 ms (−18 %, p = 0.007), message bus 0.22 vs 2.33 ms, whole namespace 9.73 vs 13.46 ms
  (−28 %, p < 0.001), marginal 9.26 vs 11.78 ms (−21 %). The ingress spends more on B (2.68 vs 2.13 ms, +26 %) because it
  compresses 3.1× more bytes. These are the first H1 verdicts of the project that pass Holm at the family level (v1's N = 5
  could not).
- *What changed since v1 (app CPU overlapped, DB 1.69× higher on B):* both seeds now carry the selective 300-word Zipf bank
  (D4 closed: B's J6 no longer ILIKE-scans ~1 300 rows), B's cards render 300-char previews instead of whole bodies (D2), the
  mix added J4 (photo) and J9 (DM) on both sides, A's read statements compute excerpts, and B gained two write gates. The
  direction of the app-tier difference is a v3 result; its size carries D1 (A's client is idealized → A's true cost is HIGHER)
  and possible contention bias (both sides measured on a saturated host).
- *Standing cost and memory* clearly favour B: idle 0.035 vs 0.121 cores (p = 0.020), application memory 73 vs 412 MiB,
  namespace 793 vs 1 268 MiB (p < 0.001) — D8 (platform GC defaults) applies to the memory rows.
- *Transfer:* 3.1× more bytes per journey on B (10.24 vs 3.27 KiB) — HTML vs JSON once card text is equal (v1: 5.25×).
- *Realtime:* B's push arrives in 40 ms median vs A's 2.3 s (outbox poll), both lossless at this load (D14).
- *Not supported by v3:* any latency claim (the host was saturated by the load generator — §3.0; v1's latency tables remain
  the reference: tails favoured A, medians favoured B on single-request pages), and any absolute CPU number free of
  contention bias.

**H2 — throughput ceiling:** NOT measured in v3 (both capacity runs collapsed at 100–150 offered j/s because the load generator
took ≈ 4 of 8 cores; §3.3). The only H2 data remain v1's (A sustained 300 j/s, B 200, knee = the 1-CPU Postgres), measured with
mix v1 (no J4/J9) and the non-selective search bank.

**H3 — static:** A's fixable HIGH/CRITICAL CVEs dropped from 13 to 0 (B 0); A remains 1.8× B's core code (two runtimes),
two images of 179 + 249 MB vs one of 41 MB, 212 KB vs 145 KB of gzip web assets; lab Lighthouse on the benchmark data favours B on mobile (feed performance 0.89 vs 0.72, TBT 11 vs 698 ms) and is near-equal on desktop (§4.1).

## 6. Anomalies and findings

1. **Load generator saturated the host (harness h6 defect, major).** J4's per-journey PNG synthesis in k6 (603 ms / ≈ 0.7 core-s
   per 256-px image, `k6-makepng-bench/`) → k6 up to 5.5 of 8 cores in demo, ≈ 4 cores at 100 j/s in capacity; latency
   contaminated, capacity collapsed (§3.0, §3.3). Not visible in the 5-VU gate (≤ 0.47 core). Not fixed tonight (rule 1).
2. **Exec-probe restarts under node load (both deployments).** B Redis (`redis-cli ping`, 1 s timeout, 50m CPU request)
   restarted during an A run, in B demo run 5 and in B capacity; A RabbitMQ (`rabbitmq-diagnostics status`, 15 s; 100m request)
   in A demo run 10 and in A capacity, A Postgres (`pg_isready`, 5 s) in A capacity; MinIO and the gomx pod once each during
   Phase 4's test suites. Consequence: invalid runs (redone), backend unreadiness (A's `/health/ready` gates RabbitMQ → 503 →
   ingress endpoints withdrawn → 0.87 % failed requests in A run 10). A deployment review item for both authors: probe
   timeouts and CPU requests of the infrastructure pods (outside the common profile, which covers the app and Postgres).
3. **D17 (new) — A's J9 sometimes targets a blocked friend.** A's Benchmark social graph (`BenchSocialGraph`, the gomx ring +
   chords over all 800 users) gives 26 of the 1 200 friend pairs of the harness login pool a friend in A's blocked band
   (`bench_user_0013…0032`, A-only); `GET /api/social/friends` lists them, the adapter picks a random friend, A's open-direct
   answers 404 (non-active users are socially invisible) and the failed pair is retried. Measured in demo: 75.4 failed vs 395 ok
   `dm_open` per A run ≈ 6 % of A's J9 journeys (the 80 demo VUs log in as `bench_user_0034…0113`, about a third of whom have
   a blocked friend) → A failed checks 0.11–0.15 % per run, B 0 %. Secondary A inconsistency: the friends list shows accounts
   the DM path treats as invisible. Recorded in the deviation register; not fixed (version under test).
4. **A's realtime ticket is single-use per replica** (one ticket, 4 handshakes → `101 101 401 401` with 2 replicas) — the
   documented Phase 7 design (in-process redeemed-jti cache, 30-s TTL), newly visible because v1's single replay hit the same
   replica.
5. **D2 byte gap halved, not closed** (feed page gzip B/A 7.9× → 3.6×): equal card text, B's card markup ≈ 6.4 KB raw vs A's
   ≈ 0.7 KB JSON per card — paradigm, not content.
6. **B private-comment WebSocket gate verified live** (thesis-v3-b): anonymous and non-member sockets 0 frames, member 1.
7. **J4 latency includes ≈ 0.94 s of k6-side work on both sides** (§3.2) — compare J4 via the per-request rows.
8. **Search bank**: rank-100 check word `cousin` 331 vs 295 (B/A 0.891, 0.9 pp outside ±10 %; deterministic, identical to
   2026-10-18); whole-bank public match sets agree within 0.8 % (90 656 vs 91 371 rows), per-word deviations up to ≈ 40 %.
9. **Tooling:** `scripts/thesis_figures.py` hard-codes "N = 5" in three titles (rendered with a patched scratch copy; patch in
   `05-analysis/`); B's binary moved to `/app/server` (artifact extraction redone); Loki SingleBinary cannot run `| json` over
   hours (line filters + chunks); both deploy scripts build inside minikube's docker, so pre-built host images are never used;
   NU1903 SSH.NET warnings in A's test projects.
10. **Operational:** a pre-round RabbitMQ restart during B's image build; host swap-thrash and probe-restart cascades whenever
    test suites ran in parallel (Phase 4) — none during measurements; the DST change at 03:00 CEST split the logs' timezone.

## 7. Threats to validity

- **Load-generator contention (dominant).** k6 shared the 8 vCPU host with the cluster and used up to 5.5 cores; latency and
  throughput are invalid for v3, and CPU-per-journey may carry a modest contention bias on both sides (same pattern for A and B,
  ABBA order). Mitigation for the next round: remove the per-journey PNG synthesis from the timed path or shrink it.
- **Run validity by restarts:** two demo runs were redone; a redo happens under the same conditions, but a selection effect
  ("only runs whose infrastructure survived") cannot be ruled out — both sides had one redo each.
- **Deviations D1–D17** bias specific numbers in known directions: D1 lowers A's server cost (so A's true app-CPU gap is
  larger), D3 favours B, D8 shapes memory, D9/D10 are A's own overheads, D12/D14 shape J4/J8, D15/D16 the write journeys,
  D17 lowers A's J9 write work slightly.
- **One host, one night, one digest per side; demo only.** No stress, HPA, auth-reads, real-SPA, GC or relay variants, no chaos.
- **Statistics:** Holm over 18 headline metrics per family; no equivalence tests ("no difference detected" ≠ equal).

## 8. Recommendations — next steps

1. **Fix the load generator first** (before any latency, stress or capacity round): generate the J4 PNG once per VU in setup
   and vary a few bytes per upload (B de-duplicates identical bytes), or run with the existing knob `--k6-env J4_IMAGE_PX=64`
   (32 ms per image instead of 603 ms) as a labelled variant; keep k6 below ~1 core (REPORT "Load generator" line) as a
   validity rule. A harness change requires a NEW round (the `--resume` path refuses changed harness/variants).
2. Then the planned extension: demo N = 10 again under the fixed harness (latency), stress:3, capacity:3 (H2), followed by the
   variant evenings (`--real-spa`, `--auth-reads`, `--hpa`, `--gc-variant`, `--relay-poll-ms 500`), soak, spike, chaos ×3.
3. Infrastructure probes (both authors): raise exec-probe timeouts / CPU requests of Redis, RabbitMQ and Postgres pods or
   switch to TCP/HTTP probes; record the change as a common-profile item.
4. D17: exclude non-active users from A's friends list (product fix) or seed friendships only among active users (seed fix);
   alternatively make the adapter skip non-active peers (harness fix).
5. Housekeeping: `thesis_figures.py` N label (patch ready), SSH.NET/Testcontainers bump in A's test projects, B artifact path
   in the static-metrics recipe.
