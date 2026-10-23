# A/B comparison v1 — forum-dotnet (A) vs gomx (B): systems, alignment, test plan, results

| | |
|---|---|
| **Status** | v1 — first *aligned* version (2026-10-17, harness `h3-2026-10-17`). Methodology, environment, dataset and harness: final for v1. Results: §9, filled from the first v1 round (`thesis/results/rounds/<stamp>-v1/REPORT.md`, generated). |
| **Authors** | Jakub Patkowski (A), Hubert Ożarowski (B); supervisor Dr inż. Kamil Żyła |
| **A** | `forum-dotnet` branch `31-feat-phase-13---ab-benchmark` (PR #30) @ `57f63d0` (the 2026-10-17 alignment) + the 2026-10-18 re-verification edits (§7.5: owner-activity indexes, idle-window pipeline) |
| **B** | `gomx` branch `feature/master-thesis` @ `f03d46a` (Hubert's B-01…B-14 + the §7.2 alignment commits `4b43557`, `f03d46a`) + the 2026-10-18 edit (§7.5, uncommitted, to be pushed to that branch) |
| **Supersedes** | `DAY0-2026-10-17.md` (its numbers stay archived; see its erratum §0 — three of its interpretations were wrong) |
| **Regenerate results** | `make ab-round` → `thesis/results/rounds/<stamp>-<label>/REPORT.md`; `make ab-report ARGS=<round dir>` re-renders a report from the raw archive |

---

## 1. What is being compared

The thesis compares two **systems** that implement the same forum (spaces, threads, nested comments, reactions,
search, profiles, friends/DMs, live updates) — not two frameworks in a micro-benchmark. Paradigm differences are the
subject of study and are deliberately **kept**; everything that is *not* the paradigm is equalized (§3–§5) or, when
equalizing it would rewrite a feature, recorded as a deviation (§6).

| Aspect | A — forum-dotnet | B — gomx |
|---|---|---|
| Rendering | **CSR SPA** (Next.js 15 used as an app shell, React Query) + **JSON API**; a page = several API calls | **SSR** (templ + goldmark + bluemonday) + HTMX (`hx-boost`); a page = one HTML document |
| Backend | .NET 10 **modular monolith** (Identity, Content, Files, Engagement, Social modules; CQRS without MediatR; Result pattern) | Go 1.26 monolith (chi, feature packages, GoF patterns) |
| Data access | EF Core (writes, aggregates + UnitOfWork) + raw ADO over SQL **views** (reads) | sqlc-generated queries over pgx |
| Database | PostgreSQL 18, one schema + migration chain per module | PostgreSQL 18, one schema, goose migrations |
| IDs / paging | ULID everywhere; **keyset** paging everywhere | int64 identity; cursor (keyset) feed, OFFSET search |
| Search | `tsvector` + `websearch_to_tsquery`, ranked (`ts_rank`), keyset | `ILIKE '%q%'` with `pg_trgm` GIN, OFFSET |
| Sessions / auth | JWT access (15 min, in memory) + rotating refresh cookie; **Argon2id** | server-side session cookie (1 SQL per authenticated request); **PBKDF2-SHA256 100k** |
| Authorization | RBAC + bitmask ACL resolved in SQL (`effective_mask`) | rule functions in Go (`CanViewCommunity`, membership) |
| Integration / async | transactional **outbox → RabbitMQ** (per-module exchanges, inbox dedupe) | **River** job queue in Postgres; **Redis pub/sub** for cross-pod WS fan-out |
| Live updates | WebSocket hub: per-view **subscriptions**, authorized per push, change notifications (client re-fetches) | WebSocket: **broadcast** to every socket, server-rendered HTML fragments (EN+PL) |
| Uploads | **presigned PUT** straight to MinIO + commit (bytes bypass the app) | multipart to the app → 202 + River job + status poll; thumbnails on read |
| Deployed pods (bench) | `backend` ×2, `frontend` ×1 (Node, serves the SPA shell), postgres, rabbitmq, minio | `gomx` ×2, postgres, redis, minio |
| Observability | Serilog JSON → Loki, OTel → Tempo, Prometheus `/metrics` | slog JSON, OTel → Tempo, Prometheus `/metrics` (port 9090) |

## 2. Research questions and the test that answers each

| | Question | Measured by |
|---|---|---|
| **H1** | Resources spent per served user journey at the same load | **closed-model** profiles `demo` (0→10→40→80 VU) and `stress` (→150 VU): CPU core-seconds and memory per pod over the exact scenario window ÷ journeys served (§5.4); latency per journey at equal concurrency |
| **H2** | Throughput ceiling: the highest offered journey rate at which journey p95 ≤ 500 ms, failures ≤ 1 % and the system keeps up | **open-model** profile `capacity` (ramping-arrival-rate 25 → 500 journeys/s in 8 steps, no think time); knee per step table |
| **H3** | Dependency surface, CVEs, code size, artifact size, dev-loop time | static metrics (trivy, cloc, image/bundle sizes, Lighthouse, dev-loop bench) — collected separately (`thesis/results/static/<date>/`), not part of the load round |

A closed model fixes the offered load (VUs × think time), so **journeys/s is near-equal on both sides by
construction** — closed profiles answer H1, never H2. H2 needs the open model, where the offered rate is the input
and the achieved rate is the output.

## 3. Test environment — the common profile (identical on both sides, verified before every run)

| Item | Value (both A and B) | How it is enforced |
|---|---|---|
| Cluster | one minikube profile `forum` (docker driver, calico CNI, ingress-nginx, metrics-server), node = WSL2 VM; A in namespace `forum-dotnet`, B in `gomx` | `make mk-up` |
| Run isolation | runs are **sequential**; the idle architecture's app is scaled to 0 (its DB/broker stay up, idle) | `ab-round.sh` |
| App pods | **2 fixed replicas, no HPA**; requests 250m/256Mi, limits 1000m/512Mi; liveness/readiness `timeoutSeconds: 5` | A manifest + `ab_apply_profile`; B overlay `deploy/k8s/bench`; **`ab_check_profile` verifies the live objects and aborts on drift** |
| Postgres | **`postgres:18` (Debian, PG 18.4)** with `shared_buffers=256MB effective_cache_size=512MB max_connections=100`; requests 250m/512Mi, limits 1000m/1Gi; same probe timing | A statefulset; B overlay `postgres-patch.yaml` (was `18-alpine`) — verified |
| Connection pool | 30 per replica (A: Npgsql `Maximum Pool Size=30`; B: `GOMX_DB_MAX_CONNS=30` → pgxpool `MaxConns`) | verified (A: secret, B: env) |
| Tracing | OFF (A `Observability__TracingEnabled=false`; B no OTLP endpoint) | verified |
| Rate limiters | neutralized for the run and recorded (A: permit limits raised to 1 000 000 then restored; B: `GOMX_AUTH_RATELIMIT=0`, `GOMX_BEACON_RATELIMIT=0`); **any 429 invalidates the run** | verified + k6 threshold |
| Compression | gzip at the **shared ingress-nginx** for both (B's in-process gzip off: `GOMX_COMPRESS=0`); the client asks for gzip on every request | B overlay + harness (`lib/wire.js`) |
| Transport | TLS on both hosts (`forum.local`, `gomx.local`, one mkcert certificate), HTTP/2 client → ingress, HTTP/1.1 ingress → pod; Secure cookies on both | `mkcert-tls.sh`, both ingresses |
| B-only flags | `GOMX_SEED=bench`, `GOMX_NOTIFY_FRIENDS_ON_POST=0` (no per-friend fan-out on new post — A has none), OAuth mock login disabled unless `GOMX_OAUTH_MOCK=1` | verified |
| Load generator | k6 v2.1.0 on the WSL host, outside the cluster, through the ingress; its own CPU/RSS sampled every 5 s and reported | `run-load-test.sh` sampler |
| Monitoring | A's kube-prometheus-stack only (Prometheus scrapes kubelet/cAdvisor for both namespaces + both apps' `/metrics`) | `make mon-up`, B ServiceMonitor applied by `gomx-bench-deploy.sh` |

## 4. Dataset — the Benchmark seeds (measured 2026-10-17, local Postgres 18 copies of both seeds)

Both seeds are deterministic (index-driven, no wall-clock randomness in shape) and every measured run starts from a
freshly reseeded database (§5.5).

| Property | A | B | Notes |
|---|---|---|---|
| Users | 800 | 800 | A: 2 admins, 10 moderators, 20 blocked; B: no staff/blocked bands |
| Spaces | 12, last 4 private | 12, last 4 private | private members: 25 each on both |
| Threads | 1 600 (16 soft-deleted) | 1 600 | B has no soft delete (hard delete, D-8) |
| Threads per space (3 hot / other public) | 315, 341, 332 / 58–77 | 330, 339, 332 / 65–68 | both: 50 % in the first three spaces, 50 % uniform (B ported A's rule on 2026-10-17) |
| Threads in private spaces | 266 | 263 | was 533 on B before the placement fix |
| Public live threads (= the harness pool, = J6 match set) | 1 317 | 1 337 | |
| Pinned | 16 (every 100th) | 16 (every 100th) | |
| Body | `# title` + `seeded` + 120–320 words from the 31-word bank; avg **1 914** chars | same generator ported; avg **1 922** chars | identical word bank (`SeedText.cs` ↔ `benchWordBank`) |
| Comments | 9 000, squared-uniform skew over threads, depth bands 60/25/10/4/1 % | 9 000, same skew (golden-ratio sequence), same depth bands | `seeded` + 12 words on both |
| Comments per public thread (mean / median / max) | 5.73 / 4 / 234 | 5.67 / 4 / 225 | the J2 workload — near-identical (pristine seeds, re-measured in-cluster 2026-10-18; the 5.72 quoted earlier came from a database that already carried a smoke run's comments) |
| Reactions | 15 000 = 11 250 thread likes + 3 750 comment likes, 0 downvotes | 15 000 = 11 250 post up-votes + 3 750 comment likes, 0 downvotes | |
| J6 hits for a bank word (e.g. `kubernetes`) | 1 313 public threads | 1 337 public posts | both "match nearly everything" (D4) |
| Social rows | 0 | 2 400 friendships + 80 pending + 2 000 DMs | not touched by the v1 mix (J9 = weight 0); A-11 adds them to A |
| Tags | 60 tags, 0–5 per thread | none | feature difference, not measured |
| Timestamps | monotonic in the index (newest = highest index = least commented) | cyclic age buckets (`seedAge(i % 500)`) | neutralized for thread journeys by the whole-corpus pool (D5) |

## 5. Workload — the shared harness (`load/k6/`, mix `v1`, harness `h3-2026-10-17`)

One k6 entrypoint (`main.js`) owns the experiment; one adapter per target owns the wire format (`targets/a.js`,
`targets/b.js`). A journey = one user-visible action; on A it is the SPA's API calls, on B one HTML round-trip.

### 5.1 Journeys and mix (closed and open profiles share the mix)

| Journey | Weight | A requests | B requests |
|---|---|---|---|
| J1 space feed | 33 % | `GET /api/content/threads?categoryId&limit=20` + `GET /api/engagement/reactions/batch` (the cards' like counts); 30 % also page 2 by keyset cursor (+ batch) | `GET /c/{slug}` (full page, 20 cards with a rendered body preview); 30 % also `GET /c/{slug}/feed?page=2&cursor=…` |
| J2 thread open | 22 % | `GET /threads/{id}` + `GET /threads/{id}/comments` + reaction batch over the comment ids (sorted, ≤ 100 — exactly the SPA) | `GET /thread/{id}` (whole tree server-rendered) |
| J6 search | 11 % | `GET /api/content/search?q=<word>&limit=20` | `GET /search?q=<word>` (20 per page) |
| nav list spaces | 8 % | `GET /api/content/categories` | `GET /communities` |
| J3 comment | 11 % | `POST /threads/{id}/comments` (201) | `POST /posts/{id}/comments` (303 → `#comment-N`, redirect not followed) |
| J5 reaction toggle | 13 % | `PUT` or `DELETE /reactions/thread/{id}` — **always the state-changing direction** (per-VU like state) | `POST /posts/{id}/vote` `dir=up` + `HX-Request` (server-side toggle, 200 fragment) |
| J7 profile | 2 % | `GET /users/{id}/stats` + `GET /users/{id}/threads` | `GET /u/{id}` |
| J8 WebSocket hold | parallel scenario | ticket → `wss …/api/realtime/ws` → subscribe one public category, hold 120 s | `wss …/ws` with the session cookie (broadcast), hold 120 s |
| J4 thread (+image), J9 DM | 0 % (P1) | implemented on A | not yet in the B adapter |

Think time 0.3–0.7 s (closed profiles only). Reads are **anonymous** on both sides; writes use one of 200 seeded
users per VU (A `bench_user_0034+`, B `benchuser0034+`), all logged in during k6 setup.

### 5.2 Selection rules (identical on both targets, layout-independent by construction)
- **Space** (J1, J8): 50 % one of the three **largest** public spaces (ranked by the crawled thread count — never by
  list order: B lists spaces by name, so "the first three" were B's three smallest), 50 % uniform over all of them.
- **Thread** (J2, J3, J5, J7): uniform over **every** public thread — both adapters crawl their feeds to the end in
  setup. A rule tied to feed position or space size would sample each seed's *layout*: the single heaviest thread
  (≈230 comments) is pinned in a small space on A but in the largest space on B, and a "30 % from page 1" rule
  opened it ≈3× more often on B (≈1.4 % vs ≈0.45 % of J2 — p99 territory). Uniform draws make the opened-thread
  comment distribution equal the seeds' (mean 5.73 A vs 5.67 B).
- **Search word**: uniform over the shared 32-token list (the seeders' word bank + `seeded`).
- **Comment body**: `seeded` + 18 bank words (same generator for both).

### 5.3 Profiles

| Profile | Model | Shape | Purpose |
|---|---|---|---|
| `smoke` | closed | 5 VU × 60 s | pipeline gate (0 % failures required) |
| `warmup` | closed | 10 VU × 60 s, no WS; 94 % reads + 6 % writes | discarded before every measured run; warms every measured code path (incl. A's tiered JIT for J3/J5 — a read-only warm-up left their p99 at 150–175 ms in the dry run); moves the dataset by only ~30 comments + ~30 reactions, identically on both sides |
| `demo` | closed | 0→10→40 (2 min)→80 (2 min)→0, 7 min; 20 WS VUs | H1 at moderate load, latency at equal concurrency |
| `stress` | closed | 50→100→150 (2 min)→0, 6.5 min; 40 WS VUs | H1 near saturation; informational thresholds |
| `capacity` | **open** | ramping-arrival-rate 25, 50, 100, 150, 200, 300, 400, 500 journeys/s; 15 s ramp + 45 s hold each (8 min); ≤ 300 VUs; 20 WS VUs | H2 knee; every request tagged with its step |

All runs stay inside A's 15-minute access-token lifetime (setup ≈ 2.5 min + ≤ 8 min).

### 5.4 What is recorded per run
- k6: per-journey count, journeys/s **over the scenario window** (k6's own rate, which also divides by the setup
  time, is kept as `per_second_k6`), med/p95/p99 per journey and per endpoint, failure rate, 429 count, WS counters,
  the window `[end of setup, + scenario length]`, and for `capacity` a per-step table + knee.
- Prometheus over **exactly that window** (`scripts/ab_window.py` → `resources.json`): CPU core-seconds, average
  and max working set **per pod**, classified app / db / bus / storage / frontend; totals for the whole namespace;
  the shared ingress-nginx (where compression happens); node busy cores. Divided by the HTTP journeys of the window
  → **CPU-ms per journey** (the H1 unit).
- Transfer: k6's `data_received{scenario:http}` / journeys → **KiB per journey on the wire** (gzip as negotiated,
  TLS included; cAdvisor exports no pod network series on this cluster).
- k6 process CPU (from `/proc` ticks) and RSS every 5 s — the load generator shares the host.
- Metadata: image + digest, git SHA/branch/dirty state of both checkouts and of the harness, live env, resources,
  Postgres image/args, seed counts before and after, pod restarts, events, the profile check output.

### 5.5 Procedure and statistics (`scripts/ab-round.sh`)
For every measured run: scale the other architecture to 0 → **reseed the target and restart its pods** (A: seed Job
`--force` + rollout restart; B: DROP/CREATE + boot seed) → verify the common profile → 60 s warm-up (discarded,
~6 % writes) → measured run → window metrics → validity checks → 60 s cool-down. Repeats are interleaved **ABBA**
(repeat 1: A,B; repeat 2: B,A; …). A run is **invalid** (excluded, listed) if k6 failed, any pod restarted, any 429
occurred, smoke/demo failure rate > 1 %, or the profile check failed. Each valid repeat is one sample; the report
gives mean ± 95 % CI (Student t) and flags whether the A and B intervals overlap.

Default plan `demo:5,stress:1,capacity:1` ≈ 3 h unattended.

### 5.6 Work per request (measured 2026-10-18 on local Postgres 18 copies of both seeds, `pg_stat_statements`)
Statements are counted with `pg_stat_statements.track = all` (nested FK/trigger statements included where noted);
"rt" = round trips. Sizes are through the cluster ingress (gzip negotiated / raw).

| Journey | A — SQL per journey (HTTP requests) | B — SQL per journey (1 HTTP request) | Wire A vs B (gzip / raw) |
|---|---|---|---|
| J1 space feed, page 1 | 4 stmts, 3 rt (category read-info, feed, reaction batch = 2 stmts in 1 rt) over 2 requests; the real SPA adds 1 slug→id lookup | **2** (community incl. a `member_count` COUNT subquery, posts) — was 3: a `GetMembership` for the anonymous viewer, fixed 2026-10-18 | 2.0 KB / 9.5 KB vs 16.0 KB / 174 KB |
| J1 page 2 (cursor) | 2 (+ batch) | 2 | |
| J2 thread open | 5 stmts, 4 rt (detail ⋈ category, thread→category read-info, tree, batch ×2) over 3 requests | 3 (post, community, comments) | 2.0 KB / 5.5 KB vs 7.5 KB / 46 KB (typical thread) |
| J6 search | 1 (anonymous: no permission query) | 1 | 1.8 / 8.0 KB vs 15.7 / 174 KB |
| J7 profile | 2 over 2 requests — `user_stats_v` **4.1 → 0.9 ms** and user threads **1.2 → 0.25 ms** after the 2026-10-18 owner indexes | 3 (`AuthorStats` 0.65 ms, account, posts) | 0.5 / 0.6 KB vs 6.1 / 30 KB |
| nav list spaces | 1 | **1** — was 13 (list + 12 membership lookups for an anonymous viewer), fixed 2026-10-18 | 0.8 / 3.2 KB vs 4.7 / 21 KB |
| J3 comment create | 5 rt: thread, `has_permission` (**1.9 ms**, live mask), BEGIN, INSERT comment + outbox (one batch), COMMIT; later: relay claim + mark (every 5 s), hub category lookup per event | 6 rt: session, post, community, BEGIN, INSERT (+ `comment_count` trigger), COMMIT; then EN+PL fragment render + Redis publish | |
| J5 reaction toggle | 7 rt: target ⋈ category, `has_permission`, existing reaction, BEGIN, INSERT/DELETE + outbox (+ `reaction_counts` trigger), COMMIT, summary; + relay + hub | 8 rt: session, BEGIN, `SELECT … FOR UPDATE`, vote dir, upsert/delete (+ `vote_score` trigger), `SUM(post_votes)` (0.38 ms on the hottest post), COMMIT, vote dir for the fragment | |
| idle, no traffic | per replica: 5 outbox relays × 1 poll / 5 s, orphan sweep / 15 min; RabbitMQ ≈ 0.1 core | per replica: River fetch 1/s + leader heartbeat + scheduler; Redis ≈ 0 | |

Also measured: every A pooled-connection use ends with Npgsql's 7-statement reset batch (no extra round trip — D9);
signed-in references for D3: B's space page runs **36** statements (20 per-card vote lookups + shell queries), A's
signed-in search/profile adds one `has_permission` per private category (4 × 1.9 ms).

Query plans (`EXPLAIN (ANALYZE, BUFFERS)`, same seeds): feeds = one index scan + LIMIT on both (A `ix_threads_feed`
0.20 ms, B `idx_posts_community` 0.61 ms); comment trees = bitmap index + hash join to users/accounts (A 1.19 ms
for 234 comments, B 0.89 ms for 225); search: A seq-scans `threads` with the tsvector filter (1 313 of 1 600 rows
match, so the planner skips the GIN) + `ts_rank` top-N, **3.9 ms**; B bitmap-ORs three trigram GINs then rechecks
`ILIKE` on all 1 600 bodies, joins accounts/communities for every row before the LIMIT, **36 ms** — the paradigm
difference D-3/D-4 under the non-selective bank D4, not an index defect. Profile: A seq scans → fixed (above).

### 5.7 Harness h4 / mix v2 (2026-10-18 evening) — what the NEXT round measures differently

Everything in §5.1–§5.6 describes mix `v1` / harness `h3`, the versions behind the §9 results. The gap-closure
session of 2026-10-18 (log: `docs/architecture/AB-TEST-GAPS-PROGRESS.md`) produced mix **`v2`** / harness
**`h4-2026-10-18`**; v2 numbers are **not comparable** to v1 and will be reported in a v2 results section.

| Change | v1 (h3) | v2 (h4) | Why |
|---|---|---|---|
| Mix weights | J1 33, J2 22, J6 11, nav 8, J3 11, J5 13, J7 2 | J1 30, J2 20, J6 11, nav 8, J3 10, J5 12, J7 2, **J4 3, J9 4** | the upload path (presigned vs server ingest, D-9) and DMs enter the comparison on both sides |
| J4 thread + photo | A only (weight 0) | A: initiate → presigned PUT (256×256 RGBA PNG generated in k6, ≈262 KB, random pixels — B would de-duplicate repeated bytes by SHA-256) → commit → create thread with `![photo](image:<id>)` → attach. B: `POST /uploads` (202) → `GET /uploads/{id}/status` every 1 s (the browser's cadence) → `POST /posts` with `![](/uploads/<id>)` in the body | one user action on both; B's journey time includes the poll waits by design (D12) |
| J9 direct message | — | A: `POST /api/social/conversations/direct` once per VU (get-or-create), then `POST …/messages`; B: `POST /messages/{friend}` (friend ids discovered per session from `GET /messages`; the bench seed's ring) | A's privacy default allows anyone, B requires friendship — both seeds satisfy their own rule; B's response is 303 either way (D13), the write is verified by the dataset delta |
| J8 receipt probes | hold only (`forum_ws_notifications` counted pushes) | every 30 s each holding VU writes one comment (A: into its subscribed category; B: anywhere — broadcast) and times the write request's **start** until the push carrying that comment id reaches its own socket: `forum_ws_receipt` (med/p95/max), `forum_ws_probes_{sent,received,lost,unresolved}`, `probe_loss_pct` (lost = unanswered after 15 s) | realtime latency and loss become measured quantities; the probe writes are ≈ 0.7 % of a demo's journeys, symmetric |
| Search bank (D4) | 32 words, every word in nearly every body | **300 words, Zipf(s = 1.2) ranks** in both seeders (A `SeedText.cs`, B `seedbench.go`) and in the k6 generators; queries drawn uniformly → median query ≈ 13 % of the corpus, 39 % of queries < 10 %, 12 % > 50 %; no bank word is a substring of another or of the fixed text (B ILIKE ≡ A token match) | J6 measures a selectivity spectrum instead of a worst case |
| Reads | anonymous | anonymous by default; `AUTH_READS=1` (`ab-round.sh --auth-reads`) sends the bearer/cookie on every read | the D3 logged-in variant is one flag |
| Token lifetime | n/a (every profile < 15 min) | A re-logs a VU in on 401 (`forum_relogins`) | enables the soak profile |
| Profiles | smoke, warmup, demo, stress, capacity | + **soak** (`SOAK_MINUTES` × `SOAK_VUS`, pass/fail), **spike** (phase-tagged base/spike/recover ×2, recovery-vs-baseline p95) | stability and burst recovery |
| Round variants | fixed 2 replicas, default GC, relay 5 s | `--hpa` (identical HPA 1–3 @ 70 % on both, runs start at 1 replica), `--gc-variant` (A `DOTNET_gcServer=0`, B `GOMEMLIMIT=384MiB`), `--relay-poll-ms N` (A outbox poll), all verified by `ab_check_profile` and recorded in `round.json`/`meta-*.json` | A-15 and D8 become measured, not argued |
| Reseed | seed only | seed + `ANALYZE` on both sides | planner statistics no longer depend on when autoanalyze fires |
| Statistics | mean ± 95 % CI, CI-overlap flag | + Welch t, exact Mann-Whitney U, Cliff's δ, Holm-adjusted verdicts (`ab_stats.py`, in every REPORT.md) | reproducible p-values from the archive |
| Fault injection | — | `ab-chaos.sh` (pod-crash / pod-delete / db-restart under demo load; per-5-s timeline, failed requests and recovery time per fault from the k6 point stream; probe loss shows lost pushes) | outbox (A) vs Redis pub/sub + River (B) under faults |
| Client side | Lighthouse local, demo data | `lighthouse.sh --cluster --target a|b --preset mobile|desktop` on the benchmark deployment; Playwright suite on A (desktop + Android emulation) | paired client-side numbers; the mobile view Hubert asked for |
| Posture | A listed private categories anonymously | A hides unreadable private categories from the list and by slug (404), like B's `/communities` | F5 parity; the SPA home feed no longer sees categories it cannot read |

Smoke of h4 on the cluster (2026-10-18, v1 seed, 5 VUs, 60 s): every new journey green on both targets; the J8
probes already show the architectural difference the v1 round could not see — **A receipt median ≈ 1.5 s
(max 1.6 s), B ≈ 5 ms (p95 ≈ 0.3 s)**: A's push waits in the transactional outbox for the relay's 5-s poll, B
publishes to Redis inside the request. Loss 0 % on both at this load.

## 6. Fairness invariants and the deviation register

### 6.1 Invariants (all enforced or verified in v1)

| # | Invariant | v1 status |
|---|---|---|
| F1 | Same functional work per measured journey | page size 20 on both (B-01); B's per-friend fan-out off (B-02); J5 toggles state on both (harness fix); same search words and match sets (B-09 + A-12 + placement fix) |
| F2 | Same dataset volume **and** distribution | §4 — volumes equal, shapes aligned (B-09 + placement fix); remaining differences D5/D6 |
| F3 | Same resources | §3 — app, Postgres (now the same image), pool; verified per run |
| F4 | Same load harness | one harness, two adapters, identical selection rules and wire conventions |
| F5 | Same security posture on measured paths | B's OAuth mock gated (B-03); A's private-category reads gated (A-12); limiters neutralized + recorded on both |
| F6 | No cache tier on one side only | none on either side (B's Redis is a pub/sub bus) |
| F7 | Compression in the same place | ingress for both; client requests gzip (k6 does not by default — Day-0 compressed nothing) |
| F8 | Tracing/logging parity | tracing off on both; one JSON log line per request on both, probes excluded on both |

### 6.2 Deviations that remain (documented, not leveled)

| # | Difference | Effect on numbers | Treatment |
|---|---|---|---|
| D1 | A's adapter is an **idealized SPA client**: thread open = 3 calls, profile = 2. The real SPA page also fetches author stats, attachments, related-by-tag search and the thread's own like state, and every card asks for its thread/category icon and avatars (cold cache ≈ 20–40 extra small calls per page); it also caches for 5 min, which the harness does not model | A's server cost is a **lower bound** | disclosed; v2: record the real SPA waterfall with a browser and replay it (+ cache model) |
| D2 | B's feed and search cards render the **whole markdown body** (20 × ~1.9 KB per page, goldmark + bluemonday per card; feed page 174 KB HTML / 16 KB gzip vs A's 8 KB / 1.5 KB JSON + 1.5 / 0.5 KB batch; search 174 / 15.7 KB vs 8.0 / 1.8 KB) — A's cards carry title + counts only | B does more work and ~8× the bytes per J1/J6 | disclosed; product difference (decision open: truncated previews) |
| D3 | Read journeys are **anonymous** | favours B (a signed-in B page adds a session lookup, per-card vote lookups and shell queries) | disclosed; **h4: `--auth-reads` variant** (reads carry the session on both sides) |
| D4 | The 31-word bank matches nearly every thread, so J6 = "match ~1 300 rows, rank/sort, return 20" on both | J6 is a worst-case, non-selective search | **closed in h4** (§5.7): 300-word Zipf bank in both seeders + k6; v1 results keep the old bank |
| D5 | Seed timestamps: A monotonic, B cyclic buckets | page-1 composition differs; thread journeys neutralized by the whole-corpus pool | recorded in `meta-B.json` |
| D6 | Social rows only in B's seed; tags and soft-deleted/blocked rows only in A's | small DB-size difference, untouched by the v1 mix | recorded |
| D7 | Write journeys measure the write only (no post-write refresh); A's SPA re-fetches the comment tree after posting, B's composer posts over the socket and appends the broadcast fragment | symmetric omission | disclosed |
| D8 | Runtime memory policy = each platform's default: A runs .NET **Server GC** (`System.GC.Server: true` in the runtimeconfig, no GC knobs); B runs Go with GOGC=100 and no GOMEMLIMIT | memory numbers (H1) partly reflect the GC policy, not only the architecture | stated next to every memory number; **h4: `--gc-variant`** (A `DOTNET_gcServer=0`, B `GOMEMLIMIT=384MiB`) for the sensitivity run |
| D9 | A's per-request stack overhead: Npgsql's 7-statement connection reset per pooled use (no extra round trip) and the A-12 read gate paid **per endpoint** (J2 = 5 statements over 3 requests vs B's 3 over 1; §5.6) | A does more, smaller SQL per journey | paradigm (JSON API + EF/Npgsql), measured and stated |
| D10 | A resolves every write's permission **live in SQL** (`has_permission` → `effective_mask`, ≈ 1.9 ms per J3/J5; the `effective_perm_cache` table is maintained but not read on that path); B's comment/vote path checks community visibility only (no query for a public space) | ≈ 0.45 ms of DB time per average journey on A | A's locked assumption 1 (RBAC + bitmask ACL in SQL); a cache read is future work, not a v1 change (security-sensitive) |
| D-1 | HTML vs JSON (+ SPA shell) | the subject of study | — |
| D-2 | Session lookup per request (B) vs in-process JWT (A) | paradigm | anonymous reads (D3) |
| D-3/D-4 | int64 + OFFSET + ILIKE/trigram (B) vs ULID + keyset + tsvector (A) | paradigm | — |
| D-5 | RabbitMQ + outbox (A) vs Redis pub/sub + River (B) | namespace totals include them | — |
| D-6 | A pushes a WS notification per reaction, B does not | A does more work per J5 | disclosed |
| D-7 | WS semantics: per-view subscription + per-push authorization (A) vs broadcast to every socket (B) | J8 = hold cost + receipt only | — |
| D-9 | Presigned upload (A) vs server ingest (B) | not in the v1 mix | **in mix v2** (J4 + photo on both, §5.7) |
| D12 | B's J4 includes the browser's 1-s status polls while the River worker stages the blob; A's J4 is five sequential calls with no wait | B's J4 journey time ≥ 1 s by construction; its request count is 3 + polls | by design (what each user sees); the per-endpoint rows separate `upload`/`upload_poll`/`thread_create` |
| D13 | B answers `POST /messages/{id}` with 303 whether or not the message was accepted; A answers 200 + id or an error | a rejected DM on B would count as success | both seeds satisfy B's friendship rule; `run.json` records the DM-table delta on both sides as the check |
| D14 | A's realtime push is relayed from the transactional outbox by a poller (`Messaging:PollIntervalMilliseconds` = 5 000 in the cluster); B publishes to Redis inside the request | A's receipt latency floor is seconds, B's milliseconds (J8 probes) — the price of at-least-once delivery; A's relay is also part of its idle cost | measured in h4; `--relay-poll-ms` records a tuned variant |
| D-10 | Argon2id (A) vs PBKDF2 (B) at login | setup only — excluded from every window | — |
| D-13 | 1 visible CPU per pod on both (.NET `ProcessorCount` = 1, Go `GOMAXPROCS` = 1) | symmetric | — |
| D-15 | A's `frontend` pod (SPA shell server) idles during the load runs | counted in A's namespace totals, not in "app" | reported separately |

## 7. What changed on 2026-10-17 to get here

### 7.1 Hubert's branch `feature/master-thesis` (B-01…B-14, verified)
B-01 page size 20/20 · B-02 `GOMX_NOTIFY_FRIENDS_ON_POST` · B-03 `GOMX_OAUTH_MOCK` · B-04 `GOMX_COMPRESS` · B-05
bench overlay · B-07 startup probe + probe timeouts in base · B-08 advisory-lock seed · B-09 seed distributions ·
B-10 version in `/stats` · B-12 PSS labels · B-13 probe/static log skip · B-14 pinned chart version. Go tests,
`go vet`, gofmt and golangci-lint (CI version 1.64.5) green.

### 7.2 Alignment edits on top of B (uncommitted in `../gomx`, to push to `feature/master-thesis`)
- **B-05a — pool size**: `GOMX_DB_MAX_CONNS` → pgxpool `MaxConns` (`postgres.go applyPoolEnv` + table test). The
  branch had put `pool_max_conns=30` into `DATABASE_URL` instead, which **crashed B at startup** (`FATAL:
  unrecognized configuration parameter "pool_max_conns"` — goose opens the raw DSN through `database/sql`);
  `secret-patch.yaml` removed.
- **Post placement** like A (50 % into the first three spaces, 50 % uniform): private posts 533 → 263, the J6 match
  set 1 068 → 1 337 public posts, pinned posts spread over 6 spaces instead of 3; seed test asserts the shape.
- **Postgres image** in the bench overlay: `postgres:18` Debian (uid 999), A's probe timing; overlay README fixed.

### 7.3 A (forum-dotnet)
- **A-12 — private-category read gate**: feed/thread/comment tree → 404, search and profile lists omit private
  threads unless the reader owns the category or holds `moderate` there (`ContentReadAccess`; integration test).
  Before: anonymous search ranked ~1 580 rows incl. private threads (security gap + 48 % more J6 work than B).
- Integration suite: the MinIO test image is pinned to the cluster's release — the Testcontainers default tag can
  no longer be pulled (MinIO withdrew its public images), which made the **whole integration suite skip silently**.
- `.dockerignore` files exclude `frontend/src-tauri` (5 GB of cargo output was sent as build context per image).
- `deploy.sh` forces a rollout for `-dirty` image tags (a same-tag rebuild otherwise keeps running the old bytes).

### 7.4 Harness and tooling (forum-dotnet `load/k6`, `scripts/`)
Whole-corpus thread pools on both adapters · shared search words · A J5 real toggle · `Accept-Encoding: gzip` on
every request · unparsed bodies discarded · scenario-only windows for journeys/s and resources · `warmup` and
`capacity` profiles · credentials stripped from archived summaries · `lib-bench.sh` (profile apply/verify, reseed,
metadata) · `ab_window.py` (Prometheus window metrics) · `ab-round.sh` (unattended interleaved round) ·
`ab_report.py` (statistics + report) · `gomx-bench-deploy.sh --fresh` · `make ab-round`, `make ab-report`.

### 7.5 Independent re-verification (2026-10-18, night) — what it changed
Every claim above was re-derived from code, SQL and live measurements (`docs/architecture/AB-START-HERE.md` §5.0).
- **B** (`app/forum/web/handlers_community.go`, `handlers_community_test.go`, `web_test.go`): anonymous viewers no
  longer pay a `GetMembership` lookup per community on `/communities` (12 queries) and per `/c/{slug}` view (1) —
  behaviour unchanged (private spaces stay hidden / 404), `TestAnonymousBrowseSkipsMembershipLookups` pins it,
  `newTestServerWithStore` lets a test wrap the store. One edit for Hubert to review.
- **A** (Content migration `AddOwnerActivityIndexes`): `ix_threads_owner_activity` and `ix_comments_owner_activity`
  (`owner_id, created_on_utc DESC, id DESC`, partial `is_deleted = false`) — profile reads were seq scans (§5.6).
- **Procedure** (`scripts/lib-bench.sh`): B's reseed now scales to 0 after the in-process seed and back to 2, so the
  measured pair never contains the pod that ran the seeder (A's pods were already restarted after its seed Job).
- **Measurement** (`ab_window.py`, `ab-round.sh`, `ab_report.py`): every run also reads an **idle window 20–50 s
  after k6 exits** → `resources.json.idle` (cores per class with no traffic) and `marginal_cpu_ms_per_journey` =
  (CPU − idle cores × window) / journeys; the report shows total, idle and marginal rows so H1 separates each
  architecture's standing cost (A: RabbitMQ + relays; B: River) from the cost of serving. Adapter behaviour is
  unchanged → harness stays `h3-2026-10-17`.
- Confirmed, not changed (recorded as D9–D11 and §8 items): A's live ACL resolution per write, Npgsql's reset batch,
  B's full-body cards, B's `member_count` COUNT per page and `SUM(post_votes)` per vote, A's 5 relay loops vs B's
  River polling when idle.

## 8. Day-0 (2026-10-17 night) vs v1
Day-0 measured unmodified gomx `master` with the v0 harness. Its numbers are archived but **must not be compared
with v1**: B served 8/10-item pages, B's search words matched its old sentence corpus, A's search ranked private
threads, the thread pools differed, half of A's J5 calls did not write, nothing was compressed, and journeys/s and
CPU included the setup phase (the "73.6 vs 76.4 journeys/s" difference was setup length — the journey counts were
equal). Details: `DAY0-2026-10-17.md` §0.

## 9. Results — round v1

> Copied verbatim from `thesis/results/rounds/20261018-0051-v1/``REPORT.md` (generated by `scripts/ab_report.py` on 2026-10-18 03:43 from the raw archive;
> re-run `make ab-report ARGS=thesis/results/rounds/20261018-0051-v1` to regenerate). Round: plan `demo:5,stress:1,capacity:1`,
> ABBA order, reseed + fresh pods + 60 s warm-up before every run, **14/14 runs valid** (0 pod restarts, 0 × 429, 0.00 %
> failed requests). Versions: A `forum-dotnet-api:git-57f63d0-dirty` (57f63d0 + the re-verification working tree), B
> `gomx:bench` = `bench-f03d46a-dirty` (f03d46a + the anonymous-membership fix); exact patches and digests in
> `thesis/results/campaign-20261017-2357/00-preflight/`. Statistics: mean ± 95 % CI over the valid repeats; ratios are B/A;
> *distinct* = the two CIs do not overlap. Every number carries the deviation register of §6.2 — in particular D1
> (idealized SPA client → A's server cost is a lower bound), D2/D11 (B's cards carry the whole rendered body → B's bytes and
> J1/J6 work), D3 (anonymous reads → favours B), D4 (non-selective search word bank → J6 is a worst case on both),
> D8 (memory numbers reflect .NET Server GC vs Go defaults), D9/D10 (A's per-request SQL overhead and live `has_permission`).
> Full campaign analysis: `thesis/results/campaign-20261017-2357/05-analysis/CAMPAIGN-REPORT.md`.

### 9.1 Pipeline validation (2026-10-17/18, not results)
See §11 — smoke runs on both targets through the real ingress, profile verification on both, one dry-run round
(`20261017-2157-dryrun-final`), the re-verification round (`20261017-2332-verify`) and the campaign gate
(`20261018-0019-gate`, both smoke runs valid). A first v1 attempt (`20261018-0029-v1-ABORTED-session-crash`) died with the
operator session during its second run and is excluded.

### 9.2 H1 — demo (closed model, 5 repeats per side)
#### Throughput, errors, overall latency
| metric | A | B | B/A | 95 % CIs |
|---|---|---|---|---|
| HTTP journeys / s (scenario window) | 91.55 ± 0.13 | 90.05 ± 0.74 | 0.98× | distinct |
| journey latency median (ms) | 8.4 ± 0.7 | 8.0 ± 1.2 | 0.95× | overlap |
| journey latency p95 (ms) | 19.2 ± 3.1 | 60.8 ± 18.3 | 3.16× | distinct |
| HTTP request p95 (ms) | 11.4 ± 1.3 | 55.6 ± 16.0 | 4.86× | distinct |
| HTTP requests per run | 76789 ± 247 | 41828 ± 341 | 0.54× | distinct |
| KiB received per journey (wire: gzip + TLS) | 2.21 ± 0.01 | 11.63 ± 0.05 | 5.25× | distinct |
| failed requests (%) | 0.00 ± 0.00 | 0.00 ± 0.00 | – | overlap |

Closed-model profiles fix the offered load (VUs × think time), so journeys/s is near-equal by design; the comparison lives in latency and in the resources each architecture spends on the same work.

#### Per-journey latency (ms)
| journey | stat | A | B | B/A | 95 % CIs |
|---|---|---|---|---|---|
| J1 space feed | med | 8.0 ± 0.9 | 11.8 ± 2.2 | 1.48× | distinct |
|  | p95 | 21.0 ± 2.6 | 51.6 ± 16.6 | 2.46× | distinct |
|  | p99 | 36.4 ± 6.1 | 111.6 ± 30.9 | 3.07× | distinct |
| J2 thread open | med | 9.6 ± 0.7 | 4.6 ± 0.7 | 0.48× | distinct |
|  | p95 | 20.0 ± 2.6 | 32.0 ± 13.4 | 1.60× | overlap |
|  | p99 | 36.2 ± 5.6 | 81.4 ± 21.6 | 2.25× | distinct |
| J3 comment create | med | 6.6 ± 0.7 | 5.4 ± 0.7 | 0.82× | overlap |
|  | p95 | 16.0 ± 4.7 | 44.2 ± 22.7 | 2.76× | distinct |
|  | p99 | 39.1 ± 26.1 | 106.0 ± 39.4 | 2.71× | distinct |
| J5 reaction toggle | med | 7.4 ± 0.7 | 4.6 ± 0.7 | 0.62× | distinct |
|  | p95 | 18.1 ± 4.4 | 49.6 ± 25.3 | 2.74× | distinct |
|  | p99 | 40.7 ± 21.2 | 142.2 ± 62.8 | 3.50× | distinct |
| J6 search | med | 11.0 ± 1.2 | 40.8 ± 4.7 | 3.71× | distinct |
|  | p95 | 18.6 ± 2.3 | 148.9 ± 59.5 | 8.01× | distinct |
|  | p99 | 30.1 ± 5.8 | 296.4 ± 99.1 | 9.83× | distinct |
| J7 profile | med | 7.0 ± 0.0 | 4.6 ± 0.7 | 0.66× | distinct |
|  | p95 | 14.3 ± 2.5 | 39.5 ± 18.9 | 2.77× | distinct |
|  | p99 | 29.0 ± 8.6 | 86.2 ± 34.3 | 2.97× | distinct |
| nav list spaces | med | 3.0 ± 0.0 | 3.4 ± 0.7 | 1.13× | overlap |
|  | p95 | 6.6 ± 1.1 | 19.4 ± 8.1 | 2.94× | distinct |
|  | p99 | 14.5 ± 4.1 | 59.6 ± 14.8 | 4.12× | distinct |

#### Resources over the scenario window (H1)
| metric | A | B | B/A | 95 % CIs |
|---|---|---|---|---|
| app CPU per journey (ms) | 5.017 ± 0.294 | 4.838 ± 0.360 | 0.96× | overlap |
| app MARGINAL CPU per journey (ms, idle cost removed) | 4.882 ± 0.293 | 4.765 ± 0.365 | 0.98× | overlap |
| app idle cores (no traffic, all replicas) | 0.012 ± 0.005 | 0.007 ± 0.002 | 0.53× | overlap |
| app avg cores | 0.459 ± 0.027 | 0.436 ± 0.029 | 0.95× | overlap |
| app memory avg (MiB, all replicas) | 390 ± 5 | 66 ± 2 | 0.17× | distinct |
| app memory max (MiB, all replicas) | 405 ± 7 | 84 ± 2 | 0.21× | distinct |
| DB CPU per journey (ms) | 2.685 ± 0.162 | 4.551 ± 0.393 | 1.69× | distinct |
| DB idle cores (no traffic) | 0.009 ± 0.003 | 0.006 ± 0.005 | 0.66× | overlap |
| DB memory avg (MiB) | 152 ± 12 | 196 ± 5 | 1.29× | distinct |
| bus CPU per journey (ms) — A RabbitMQ / B Redis | 1.519 ± 0.125 | 0.141 ± 0.008 | 0.09× | distinct |
| bus idle cores (no traffic) | 0.101 ± 0.055 | 0.007 ± 0.005 | 0.07× | distinct |
| bus memory avg (MiB) | 91 ± 7 | 7 ± 0 | 0.08× | distinct |
| A frontend pod memory avg (MiB) | 59 ± 1 | – | – |  |
| WHOLE namespace CPU per journey (ms) | 9.247 ± 0.538 | 9.544 ± 0.751 | 1.03× | overlap |
| WHOLE namespace MARGINAL CPU per journey (ms) | 7.884 ± 0.786 | 9.315 ± 0.808 | 1.18× | overlap |
| WHOLE namespace idle cores (no traffic) | 0.125 ± 0.062 | 0.021 ± 0.009 | 0.17× | distinct |
| WHOLE namespace memory avg (MiB) | 772 ± 11 | 357 ± 5 | 0.46× | distinct |
| ingress-nginx CPU per journey (ms, shared edge) | 1.484 ± 0.099 | 2.668 ± 0.147 | 1.80× | distinct |
| node busy cores avg (context) | 2.97 ± 0.12 | 2.93 ± 0.15 | 0.99× | overlap |

#### WebSocket holds (J8) — semantics differ (A: per-view subscription; B: broadcast to every socket)
| metric | A | B |
|---|---|---|
| subscribed | 80 ± 0 | 80 ± 0 |
| notifications | 34467 ± 3055 | 83462 ± 2035 |
| errors | 0 ± 0 | 0 ± 0 |

Load generator (k6 on the same host): max CPU A 44 % / B 74 % of one core, max RSS A 652 MiB / B 471 MiB.

### 9.3 Stress (closed model, informational, 1 repeat per side)
#### Throughput, errors, overall latency
| metric | A | B | B/A | 95 % CIs |
|---|---|---|---|---|
| HTTP journeys / s (scenario window) | 189.60 | 155.95 | 0.82× |  |
| journey latency median (ms) | 10.0 | 54.0 | 5.40× |  |
| journey latency p95 (ms) | 41.0 | 454.0 | 11.07× |  |
| HTTP request p95 (ms) | 21.0 | 415.0 | 19.74× |  |
| HTTP requests per run | 146807 | 67196 | 0.46× |  |
| KiB received per journey (wire: gzip + TLS) | 2.25 | 11.72 | 5.22× |  |
| failed requests (%) | 0.00 | 0.00 | – |  |

Closed-model profiles fix the offered load (VUs × think time), so journeys/s is near-equal by design; the comparison lives in latency and in the resources each architecture spends on the same work.

#### Per-journey latency (ms)
| journey | stat | A | B | B/A | 95 % CIs |
|---|---|---|---|---|---|
| J1 space feed | med | 11.0 | 52.0 | 4.73× |  |
|  | p95 | 46.0 | 274.0 | 5.96× |  |
|  | p99 | 129.0 | 424.0 | 3.29× |  |
| J2 thread open | med | 12.0 | 41.0 | 3.42× |  |
|  | p95 | 47.0 | 231.0 | 4.91× |  |
|  | p99 | 139.0 | 362.5 | 2.61× |  |
| J3 comment create | med | 8.0 | 67.0 | 8.38× |  |
|  | p95 | 33.0 | 333.7 | 10.11× |  |
|  | p99 | 96.0 | 508.7 | 5.30× |  |
| J5 reaction toggle | med | 9.0 | 84.0 | 9.33× |  |
|  | p95 | 39.0 | 474.0 | 12.15× |  |
|  | p99 | 121.7 | 631.2 | 5.19× |  |
| J6 search | med | 12.0 | 209.0 | 17.42× |  |
|  | p95 | 33.0 | 1492.9 | 45.24× |  |
|  | p99 | 87.4 | 2163.3 | 24.76× |  |
| J7 profile | med | 8.0 | 42.0 | 5.25× |  |
|  | p95 | 33.8 | 251.4 | 7.44× |  |
|  | p99 | 90.8 | 351.5 | 3.87× |  |
| nav list spaces | med | 3.0 | 21.0 | 7.00× |  |
|  | p95 | 16.0 | 114.0 | 7.12× |  |
|  | p99 | 57.0 | 199.0 | 3.49× |  |

#### Resources over the scenario window (H1)
| metric | A | B | B/A | 95 % CIs |
|---|---|---|---|---|
| app CPU per journey (ms) | 4.414 | 4.575 | 1.04× |  |
| app MARGINAL CPU per journey (ms, idle cost removed) | 4.367 | 4.527 | 1.04× |  |
| app idle cores (no traffic, all replicas) | 0.009 | 0.007 | 0.85× |  |
| app avg cores | 0.837 | 0.713 | 0.85× |  |
| app memory avg (MiB, all replicas) | 392 | 97 | 0.25× |  |
| app memory max (MiB, all replicas) | 411 | 135 | 0.33× |  |
| DB CPU per journey (ms) | 2.570 | 4.882 | 1.90× |  |
| DB idle cores (no traffic) | 0.022 | 0.000 | 0.00× |  |
| DB memory avg (MiB) | 251 | 289 | 1.15× |  |
| bus CPU per journey (ms) — A RabbitMQ / B Redis | 0.887 | 0.088 | 0.10× |  |
| bus idle cores (no traffic) | 0.143 | 0.007 | 0.05× |  |
| bus memory avg (MiB) | 98 | 8 | 0.08× |  |
| A frontend pod memory avg (MiB) | 60 | – | – |  |
| WHOLE namespace CPU per journey (ms) | 7.880 | 9.555 | 1.21× |  |
| WHOLE namespace MARGINAL CPU per journey (ms) | 6.942 | 9.460 | 1.36× |  |
| WHOLE namespace idle cores (no traffic) | 0.178 | 0.015 | 0.08× |  |
| WHOLE namespace memory avg (MiB) | 883 | 482 | 0.55× |  |
| ingress-nginx CPU per journey (ms, shared edge) | 1.425 | 2.724 | 1.91× |  |
| node busy cores avg (context) | 4.34 | 3.98 | 0.92× |  |

#### WebSocket holds (J8) — semantics differ (A: per-view subscription; B: broadcast to every socket)
| metric | A | B |
|---|---|---|
| subscribed | 160 | 308 |
| notifications | 129446 | 259426 |
| errors | 0 | 148 |

Load generator (k6 on the same host): max CPU A 110 % / B 102 % of one core, max RSS A 1013 MiB / B 728 MiB.

### 9.4 H2 — capacity (open model, 1 repeat per side)
#### Throughput, errors, overall latency
| metric | A | B | B/A | 95 % CIs |
|---|---|---|---|---|
| HTTP journeys / s (scenario window) | 197.34 | 140.03 | 0.71× |  |
| journey latency median (ms) | 18.0 | 295.0 | 16.39× |  |
| journey latency p95 (ms) | 1056.0 | 2859.0 | 2.71× |  |
| HTTP request p95 (ms) | 531.2 | 2689.2 | 5.06× |  |
| HTTP requests per run | 188559 | 74164 | 0.39× |  |
| KiB received per journey (wire: gzip + TLS) | 2.28 | 11.72 | 5.13× |  |
| failed requests (%) | 0.00 | 0.00 | – |  |

Closed-model profiles fix the offered load (VUs × think time), so journeys/s is near-equal by design; the comparison lives in latency and in the resources each architecture spends on the same work.

#### Per-journey latency (ms)
| journey | stat | A | B | B/A | 95 % CIs |
|---|---|---|---|---|---|
| J1 space feed | med | 21.0 | 257.0 | 12.24× |  |
|  | p95 | 1111.0 | 2298.0 | 2.07× |  |
|  | p99 | 1707.3 | 3582.0 | 2.10× |  |
| J2 thread open | med | 21.0 | 264.0 | 12.57× |  |
|  | p95 | 1062.6 | 2713.0 | 2.55× |  |
|  | p99 | 1375.9 | 3117.9 | 2.27× |  |
| J3 comment create | med | 15.0 | 381.5 | 25.43× |  |
|  | p95 | 993.4 | 3671.8 | 3.70× |  |
|  | p99 | 1297.8 | 4090.4 | 3.15× |  |
| J5 reaction toggle | med | 17.0 | 442.0 | 26.00× |  |
|  | p95 | 1555.4 | 2906.0 | 1.87× |  |
|  | p99 | 1966.2 | 3295.6 | 1.68× |  |
| J6 search | med | 19.0 | 804.0 | 42.32× |  |
|  | p95 | 449.0 | 2913.4 | 6.49× |  |
|  | p99 | 630.6 | 3716.7 | 5.89× |  |
| J7 profile | med | 12.0 | 294.0 | 24.50× |  |
|  | p95 | 541.0 | 2744.7 | 5.07× |  |
|  | p99 | 750.6 | 3084.7 | 4.11× |  |
| nav list spaces | med | 6.0 | 99.0 | 16.50× |  |
|  | p95 | 328.0 | 971.7 | 2.96× |  |
|  | p99 | 415.1 | 1171.5 | 2.82× |  |

#### Resources over the scenario window (H1)
| metric | A | B | B/A | 95 % CIs |
|---|---|---|---|---|
| app CPU per journey (ms) | 3.858 | 4.356 | 1.13× |  |
| app MARGINAL CPU per journey (ms, idle cost removed) | 3.814 | 4.285 | 1.12× |  |
| app idle cores (no traffic, all replicas) | 0.009 | 0.010 | 1.14× |  |
| app avg cores | 0.761 | 0.610 | 0.80× |  |
| app memory avg (MiB, all replicas) | 390 | 103 | 0.26× |  |
| app memory max (MiB, all replicas) | 424 | 201 | 0.47× |  |
| DB CPU per journey (ms) | 2.429 | 4.620 | 1.90× |  |
| DB idle cores (no traffic) | 0.027 | 0.007 | 0.26× |  |
| DB memory avg (MiB) | 254 | 266 | 1.05× |  |
| bus CPU per journey (ms) — A RabbitMQ / B Redis | 0.830 | 0.093 | 0.11× |  |
| bus idle cores (no traffic) | 0.060 | 0.009 | 0.15× |  |
| bus memory avg (MiB) | 102 | 8 | 0.08× |  |
| A frontend pod memory avg (MiB) | 59 | – | – |  |
| WHOLE namespace CPU per journey (ms) | 7.126 | 9.079 | 1.27× |  |
| WHOLE namespace MARGINAL CPU per journey (ms) | 6.637 | 8.888 | 1.34× |  |
| WHOLE namespace idle cores (no traffic) | 0.097 | 0.027 | 0.28× |  |
| WHOLE namespace memory avg (MiB) | 888 | 464 | 0.52× |  |
| ingress-nginx CPU per journey (ms, shared edge) | 1.275 | 2.374 | 1.86× |  |
| node busy cores avg (context) | 4.04 | 3.49 | 0.86× |  |

#### WebSocket holds (J8) — semantics differ (A: per-view subscription; B: broadcast to every socket)
| metric | A | B |
|---|---|---|
| subscribed | 80 | 222 |
| notifications | 63193 | 107830 |
| errors | 0 | 152 |

Load generator (k6 on the same host): max CPU A 127 % / B 101 % of one core, max RSS A 2328 MiB / B 1479 MiB.

#### Capacity (open model): offered vs achieved journeys/s per step (H2)
| step | offered j/s | A achieved | A p95 ms | A failed % | B achieved | B p95 ms | B failed % |
|---|---|---|---|---|---|---|---|
| c01 | 25 | 25.0 | 13 | 0.00 | 25.0 | 30 | 0.00 |
| c02 | 50 | 50.0 | 12 | 0.00 | 50.0 | 32 | 0.00 |
| c03 | 100 | 100.0 | 14 | 0.00 | 100.0 | 34 | 0.00 |
| c04 | 150 | 150.0 | 16 | 0.00 | 149.6 | 158 | 0.00 |
| c05 | 200 | 199.3 | 34 | 0.00 | 199.6 | 355 | 0.00 |
| c06 | 300 | 300.0 | 77 | 0.00 | 209.1 | 3210 | 0.00 |
| c07 | 400 | 393.2 | 595 | 0.00 | 204.4 | 2895 | 0.00 |
| c08 | 500 | 404.5 | 1658 | 0.00 | 203.8 | 3507 | 0.00 |

**Sustained throughput** (highest step before the knee: p95 > 500 ms, failures > 1 % or achieved < 95 % of offered): A 300 j/s, B 200 j/s. Knees: A [{'step': 'c07', 'offered_jps': 400, 'reason': 'p95>500ms'}], B [{'step': 'c06', 'offered_jps': 300, 'reason': 'p95>500ms'}].


## 10. Runbook — the first v1 round (new session)

```bash
cd ~/projects/forum-dotnet
make mk-up                                    # cluster up (idempotent)
make mon-up && make mon-check                 # Prometheus/Grafana (idempotent)
make mk-deploy ARGS=--seed-benchmark          # A: rebuild + deploy (only if A's code changed since)
make gomx-deploy ARGS=--reset-db              # B: rebuild + deploy + reseed (use --fresh only to recreate the namespace/volumes)
TARGET=a bash scripts/run-load-test.sh smoke && TARGET=b bash scripts/run-load-test.sh smoke    # gate: 0 % failures on both
make ab-round ARGS="--plan demo:5,stress:1,capacity:1 --label v1"                      # ≈ 3 h, unattended
# → thesis/results/rounds/<stamp>-v1/REPORT.md  (+ round-summary.json and every raw file)
```
Keep `make tunnels` and Grafana browsing off during the round (they add node CPU). If a run is invalid, the report
lists why; re-run just that profile with `--plan <profile>:<n>`.

## 11. Validation performed on 2026-10-17 (evening)
- B: `go vet ./...`, `go test ./app/... ./internal/...` (incl. Postgres integration tests), gofmt, golangci-lint
  1.64.5 — all green; bench overlay renders (no HPA, `GOMX_DB_MAX_CONNS=30`, `postgres:18`).
- A: `dotnet build`, full `dotnet test` (unit + architecture + integration, now actually executed), new A-12
  integration test green.
- Seeds: both Benchmark seeds generated locally and compared in SQL (§4).
- Harness: smoke on both adapters locally (A on the local API, B on a local container) — 0 % failures; full-corpus
  pools 1 317 (A) / 1 337 (B) threads.
- Cluster (minikube `forum`, 2026-10-17 21:30–22:10): A rebuilt + redeployed (A-12 image), B deployed `--fresh` from
  `feature/master-thesis` + alignment edits (Postgres 18.4 Debian on both, verified via `SELECT version()`);
  `ab_check_profile` all-ok on both sides; ingress gzip verified (A search 8 016 → 1 804 B, B search 172 609 →
  15 369 B); B-08 seen live (two pods booted together, exactly one seeded: 800/1600/9000/15000, 263 private posts).
- Smoke through ingress + TLS + gzip on both targets: 0 % failures, pools 1 317 (A) / 1 337 (B) threads.
- Dry-run round `thesis/results/rounds/20261017-2157-dryrun-final/` (`--plan smoke:1`): reseed + fresh pods, profile
  check, warm-up, measured run, Prometheus window metrics, validity flags and REPORT.md generated for both targets —
  the full pipeline works unattended. (Smoke-load numbers are pipeline validation, not results.) It also showed why
  the warm-up must include writes: with a read-only warm-up A's app CPU per journey was 16.7 ms and J3/J5 p99
  150–175 ms; with the 6 %-write warm-up 10.6 ms.
- Harness `h3` (layout-independent thread/space selection, §5.2) validated afterwards with one smoke per target
  (0 % failures).
- **2026-10-18 re-verification gates** (independent session): A `dotnet build` 0 warnings, **248 tests / 0 failed /
  0 skipped**, `dotnet format` clean — before and after the owner-index migration; B lint:go (check-generated, templ
  fmt, gofmt, vet, golangci-lint 1.64.5), `go test -race ./...` and the Postgres-lane adapter tests green in the CI
  image at `f03d46a` and with the new edit; overlay renders (0 HPA). Both images rebuilt from the committed trees
  (+ edits) and redeployed (`forum-dotnet-api:git-57f63d0-dirty`, `gomx:bench-f03d46a-dirty`); pristine reseed
  re-checked (A 1 317 / 5.73 / 4 / 234, thread-id set identical to the previous seed; B 1 337 / 5.67 / 4 / 225);
  `ab_check_profile` 34/34 ok; smokes A 1 221 reqs · p95 11 ms · 0 % failed, B 711 reqs · p95 38 ms · 0 % failed
  (B's run overlapped A's image build); verify round `thesis/results/rounds/20261017-2332-verify/` (`--plan
  smoke:1`): both runs `valid=True`, REPORT.md with the new idle/marginal rows — e.g. A app 12.7 ms/journey total,
  11.0 marginal, idle 0.017 cores; B 4.6 / 3.8 / 0.008; A bus (RabbitMQ) idle 0.111 cores = 11 ms of the 26.9 ms
  "namespace CPU per journey" at smoke load (marginal namespace 12.9 vs B 6.3 ms). Smoke numbers are pipeline
  validation, not results.

## 12. Threats to validity and the road to a full comparison

- **Single host.** Cluster, load generator and monitoring share one WSL2 VM. Mitigations: sequential runs, idle app
  scaled to 0, ABBA order, reseed + fresh pods per run, k6 CPU/RSS recorded in every report (a saturated k6
  invalidates a capacity knee — extend or rerun with a lower `CAP_MAX_VUS`/fewer steps if k6 nears one core).
- **Closed vs open model.** demo/stress fix the offered load (journeys/s equal by design) — only latency and
  resources per journey are compared there; H2 uses the open `capacity` profile only.
- **Idealized client (D1)** and **anonymous reads (D3)** bias A's server cost down and B's up respectively; both
  are stated with every table and planned as v2 variants.
- **Small N.** v1 uses 5 repeats per side (demo) and 1 (stress, capacity); the report's "CIs overlap/distinct" is a
  conservative heuristic, not a significance test. Hypothesis-tested tables need N ≥ 10 and Welch / Mann-Whitney.
- **Non-selective search (D4)** turns J6 into a worst-case query on both sides.
- **Standing vs marginal cost.** "CPU per journey" over a window includes each architecture's idle cost (A's
  RabbitMQ alone ≈ 0.1 core; relays, sweeps, River polling). At low load this dominates (smoke: A "bus" 10 ms per
  journey of pure idle). Since 2026-10-18 every run records an idle window and the report shows total, idle and
  marginal values — quote the marginal value for "cost of serving", the total for "cost of running".
- **The capacity knee is a DB knee (measured 2026-10-18).** With the common profile's 1-CPU Postgres, B's database reaches
  1.00 core at 300 offered journeys/s and A's 0.93 core at 400 (`campaign-20261017-2357/03-round/capacity-per-step-cpu.json`)
  while both app tiers stay below their 2-core budgets — the H2 ceilings (A ≈ 400, B ≈ 205 j/s) describe the shared DB
  sizing and the per-journey DB cost (B 1.7–1.9× A under D4), not an app-tier limit. A different DB size moves both.
- **B's live updates are lossy under load.** The hub closes slow consumers; stress/capacity holds saw 148/152 socket errors
  on B (0 on A). J8 receipt/loss is not measured in v1.
- **Infrastructure.** MinIO images can no longer be pulled; the cluster's images are backed up at
  `~/.cache/forum-dotnet/images/minio-images-2025-09.tar` (restore: `minikube -p forum image load <tar>`). GitHub CI
  needs a mirror or its integration job skips silently.

What a *full* comparison still needs (owners, priorities): `docs/architecture/AB-START-HERE.md` §8.
