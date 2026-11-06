# Prompt: A/B v5 alignment on Architecture A (one Fable session, max effort)

> Paste everything below the line into a new Claude Code session opened in `~/projects/forum-dotnet`, on the
> branch prepared for it (see "Branch" below). Written 2026-11-06.

---

You are working on **Architecture A** of a master's thesis: the decoupled React SPA + .NET 10 forum in this repo.
It is benchmarked against **Architecture B** (`../gomx`, Hubert's Go + Templ + HTMX SSR monolith). Your job in this
session: bring A to parity with B's latest optimized version **without breaking fairness**, and get the benchmark
harness ready for the final v5 rounds. Reply to Jakub in **Polish**; all code, comments and repo docs in
**English**.

## Hard rules

- **Never commit, tag or push** (CLAUDE.md). At the end, stage your changes with `git add` and list them.
- Do not edit anything in `../gomx` — Hubert's own session makes B's changes. You may read it.
- `docs/specs/READ-CACHE-CONTRACT.md` is **binding**. Implement it exactly. If the code cannot follow a row,
  stop and report it; do not change the contract on your own.
- Central Package Management only (`backend/Directory.Packages.props`), module boundaries enforced by
  `Forum.ArchitectureTests`, Result pattern, `internal` everywhere except `Contracts/`.
- This host is memory-constrained (WSL, about 12 GB): **never run two test suites or a test suite and a load test
  at the same time**; run the full `dotnet test` once at the end, not repeatedly.

## Branch

Jakub prepares it before this session: Hubert's branch `31-feat-phase-13---ab-benchmark-v2` fast-forwarded into
`31-feat-phase-13---ab-benchmark`, his own pending docs committed, then a new branch
`33-feat-phase-14---ab-v5-alignment` created from it. Check with `git status` / `git log -3` that you are on it and
that the tree is clean before you start; if not, stop and ask.

## Read first (in this order)

1. `CLAUDE.md` (project rules and state).
2. `docs/specs/READ-CACHE-CONTRACT.md` (binding cache contract for both sides).
3. Memory note `hubert-v4-review-2026-11-06.md` (in your memory directory): the full review of Hubert's work —
   branches, his rounds, what his optimizations do, what is unfair. Do not re-derive it.
4. `docs/specs/B-BENCHMARK-GUARDRAILS.md`, `thesis/results/AB-COMPARISON-v1.md` §5.6–§6 (invariants F1–F8 and
   the deviation register), `docs/architecture/AB-START-HERE.md`, `load/k6/targets/README.md`.
5. B's code for reference (read-only): `../gomx` branch `origin/feature/improvements-libraries` —
   `internal/cache/cache.go`, `app/forum/feed/service.go`, `app/forum/web/handlers_search.go`,
   `app/forum/web/handlers_feed.go`, `app/forum/shared/postgres/migrations/00025_*.sql` and `00027_*.sql`,
   `docs/REVIEW-FIXES.md` §4a and §5.

## Facts already established (do not re-measure before you start)

- Hubert's best A/B round so far: `thesis/results/rounds/20261029-0043-v4-16core` (A `git-64cd5e2` vs B
  `thesis-v4-b`, demo N = 10 per side, all valid). His B-only rounds of 2026-11-02 compare B `thesis-v4-b` with
  `feature/improvements-libraries` (`b7f8fca`): B app CPU per journey 2.39 → 1.38 ms, Postgres CPU 2.68 → 1.50 ms,
  stress core p95 782 → 8 ms, capacity 200 → 400 journeys/s. A in the same harness: app 4.82 ms, DB 2.74 ms,
  stress core p95 about 70 ms, capacity 200–250 journeys/s.
- Per HTTP request, A's app CPU (2.35 ms) is close to B `thesis-v4-b` (2.08 ms); A's per-journey cost is higher
  mainly because the SPA issues 2.05 requests per journey vs B's 1.17. That is A's design, not a bug — leave it.
- B's improvements are of three kinds: (1) data microcaches C1–C3 (now mirrored by the contract), (2) honest but
  one-sided tuning: search split by sort with early stop on `(created_at, id)`, GIN `fastupdate = off`,
  `fillfactor 90` on hot-updated tables, render-path work, (3) neutral: `GOMAXPROCS = 1` (Go 1.26's default
  under a 1-CPU limit is **2**, so our D-13 "1/1" was wrong for every round up to v4), `GOMEMLIMIT` = 90 % of the
  cgroup limit (never binds at about 70 MiB), PGO from an in-memory test (not from k6).
- J6 asymmetry: B's default search sort is **newest first** and stops after 20 matches for common words; A sorts
  by `ts_rank` and must score every match (`ContentQueries.cs` search query, `ThreadSearchCursor`).
- A's outbox relay sleeps `Messaging:PollIntervalMilliseconds` = 5 000 ms between polls
  (`Forum.Infrastructure/Messaging/RabbitMq/OutboxRelayService.cs`), which is why A's WebSocket push median is
  about 1.6–2.6 s vs B's 12–20 ms.
- RabbitMQ runs with the default Erlang scheduler busy-waiting; the bus is about 13 % of A's namespace CPU.

## Part A — application changes in A

**A1. Read cache per the contract (C1, C2, C3).**
- Use .NET's `HybridCache` (`Microsoft.Extensions.Caching.Hybrid`, version matching the .NET 10 SDK, added to CPM)
  as an in-process L1 cache (register no distributed L2), or an equivalent that gives the same mechanics: bounded
  size, built-in stampede protection, tag or prefix invalidation. Check the real API of the installed version
  before you design around it.
- Put the cache in the **Content** module's application/infrastructure layer behind a small module-internal port
  (e.g. `IContentReadCache`), wired in `ContentModule`. Cache **inside the query handlers after the read gate**
  (`GetThreadFeed`, `GetCommentTree`, `SearchThreads`), never as response middleware.
- Keys exactly as in the contract: C1 = (category id, sort, cursor, limit), C2 = thread id, C3 = (viewer scope
  `anonymous`/user id, normalized query, sort, cursor, limit). Verify that every cached DTO is identical for all
  viewers inside its key; anything viewer-specific must not enter the cached object.
- Invalidation exactly as the contract's §3 table, from the write handlers (`CreateThread`, `UpdateThread`,
  `DeleteThread`, `PinThread`, `ChangeThreadCategory`, `CreateComment`, `UpdateComment`, `DeleteComment`,
  `Create/Update/DeleteCategory`), **after** the write is durable, local only. Add the generation guard: a value
  computed across an invalidation is not stored. Avoid per-hit deserialization cost (for `HybridCache`, cached
  types marked immutable so L1 returns the stored instance) — check it.
- Switch `ReadCache:Enabled` (default `false` in `appsettings.json`), TTLs in options (2 s / 2 s / 3 s) but fixed by
  the contract; when disabled the handlers call the store directly (no lookup, no stampede layer).
- Metric `forum.read_cache.lookups` (tags `cache` = feed|comments|search, `result` = hit|miss|bypass) via
  `ForumMetrics` (closed tag set), documented in `docs/architecture/OBSERVABILITY-CONTRACT.md`.

**A2. Search ordering parity (J6).**
- Add `sort=newest|relevance` to `GET /api/content/search`; `newest` orders by `created_on_utc DESC, id DESC`
  with its own keyset cursor variant (the sort travels inside the cursor; a cursor of the other sort → 422).
  Keep the A-12 read gate on every path. Default for the API stays `relevance` unless Jakub decides otherwise.
- Add the index the planner needs to stop early (e.g. on `threads (created_on_utc DESC, id DESC) WHERE
  is_deleted = false`, adjusted to how `thread_feed_v` is planned).
- Check plans with `EXPLAIN (ANALYZE, BUFFERS)` on a Benchmark seed (local: `make seed ARGS=--benchmark` →
  `forum_net_bench`) for a rank-1, a rank-50 and a rank-300 word of the shared Zipf bank. Npgsql auto-prepare
  (`Max Auto Prepare=20`) can freeze a generic plan after five executions — the trap Hubert hit on B. Verify
  plan stability after more than five executions per word; if a generic plan loses the early stop, take that
  command out of auto-prepare or force custom plans for it, and document the choice.
- SPA: the search page gets a sort switch (Newest / Most relevant) with **Newest as the default**, like B's UI, so
  the k6 adapter keeps mirroring the real SPA. Update the frontend types, the page and its tests.

**A3. DB tuning equal to B's.**
- Raw-SQL migration (Content): `fastupdate = off` on `forum_content.ix_threads_search`, then flush its pending list
  (`gin_clean_pending_list`).
- `fillfactor = 90` on the tables whose rows are UPDATEd on measured journeys — at least
  `forum_engagement.reaction_counts` (trigger-maintained counters); check `forum_social.conversation_participants`
  (J9 seat updates) and any other counter table. Verify with `pg_stat_user_tables.n_tup_hot_upd` on a Benchmark
  seed under a short load, before vs after. The bench reseeds after migrations, so new pages get the free space.

**A4. Outbox relay wake-up (A's equal-effort item for the WebSocket push).**
- After a transaction that inserted outbox rows **commits**, signal that module's relay on the same replica so it
  claims immediately instead of sleeping up to 5 s; keep the poll as the fallback for other replicas and crashes.
  Mind the paths that write outbox rows inside an explicit transaction (consumer host with inbox rows): signal on
  commit, not on `SavedChanges`.
- Switch `Messaging:WakeRelayOnWrite` (default `true`), keep `--relay-poll-ms` meaningful. Integration test: with
  a 5 000 ms poll and wake-up on, a REST write reaches its consumer and the realtime hub in well under 1 s.

**A5. RabbitMQ without busy-waiting.**
- `RABBITMQ_SERVER_ADDITIONAL_ERL_ARGS="+sbwt none +sbwtdcpu none +sbwtdio none"` in `k8s/rabbitmq/statefulset.yaml`
  and `compose.yaml`. Verify the broker starts and stays ready.

**Out of scope (do not do):** Npgsql `No Reset On Close` (advisory-lock risk in the Files sweeper), merging J2's
three calls into one endpoint, GC knobs, Redis or any distributed cache, response `OutputCache`.

## Part B — harness and scripts (this repo)

- **B1.** `HARNESS_VERSION` → `h7-<today>`; document in `load/k6/targets/README.md` what changed since h6: B reads
  sent as boosted HTMX navigations by default (`BOOSTED=0` = full pages), B's J4 adaptive polling, J4 PNG
  fixture, read-cache variant, A's J6 `sort=newest`, generator gate.
- **B2.** J4 PNG: stop generating the 256×256 base image at run time. Ship a pre-generated fixture under
  `load/k6/` and load it with `open(…, 'b')` in the init context; keep the unique `tEXt` chunk per upload (B must
  not be able to de-duplicate). Remove the misleading `setup()` pre-warm. Check k6 RSS with the capacity
  profile's VU count.
- **B3.** Generator sanity gate in `scripts/ab-round.sh`: today any 5-s sample above 2 cores invalidates the run,
  which flagged every capacity run although k6 used 23–55 % of one core until the knee (the spike follows the
  saturation: the arrival-rate executor adds VUs). New rule: always record k6 mean and peak CPU in `run.json`;
  demo/stress invalid if mean > 1 core or peak > max(2 cores, 25 % of host cores); capacity judged only up to
  the end of the last healthy step (use the step schedule and `setupEndedAtMs`).
- **B4.** Read-cache variant end to end: `scripts/ab-round.sh --read-cache on|off` (default `off`), `lib-bench.sh`
  sets `ReadCache__Enabled` on A's backend and `GOMX_READ_CACHE` on the gomx deployment (same pattern as
  `AB_RELAY_POLL_MS`), `ab_check_profile` verifies both, `meta-*.json` + `round.json` + `REPORT.md` header record
  it, `--resume` refuses a mismatch. Add `ReadCache__Enabled: "false"` to `k8s/backend/configmap.yaml`.
- **B5.** Record-only runtime facts in `meta-B.json`: B's `GOMAXPROCS` (Prometheus
  `go_sched_gomaxprocs_threads{namespace="gomx"}`) and whether its log shows the cgroup `GOMEMLIMIT` line; A's
  visible processor count. Also record `Messaging__WakeRelayOnWrite` and the RabbitMQ Erlang args for A.
- **B6.** `scripts/gomx-bench-deploy.sh`: Hubert's `podman save gomx:bench | mk image load - >/dev/null 2>&1 || true`
  becomes opt-in (`GOMX_IMAGE_FROM_PODMAN=1`), errors visible when enabled; document it in `.env.example`.
- **B7.** `load/k6/targets/b.js`: fix the stale "1 s polling" comments; make the `BOOSTED` comment accurate (for
  anonymous reads it saves shell rendering and about 3 KiB per journey, not DB queries — `withShell` only queries
  for signed-in users). `targets/a.js`: J6 sends `sort=newest` in both the ideal and the `REAL_SPA` paths.

## Part C — docs

- `thesis/results/AB-COMPARISON-v1.md` §6: F1 gains the J6 ordering rule; F6 is rewritten ("the same read cache
  on both sides or none, switched per round — `READ-CACHE-CONTRACT.md`"); new or corrected deviation rows:
  boosted B navigation (h7), J4 adaptive polling (update D12), **D-13 correction** (B's GOMAXPROCS was 2 through
  v4; v5 sets 1), D8 note (B sets GOMEMLIMIT programmatically, never binds; the env-only profile check missed it),
  B's one-sided tuning list (00025, search split, fastupdate, fillfactor, render work) and A's equal-effort pass
  (A1–A5). Add a short "v5 set-up" section with no results yet.
- `docs/specs/B-BENCHMARK-GUARDRAILS.md` → v5: measured versions `thesis-v5-a` / `thesis-v5-b`, the cache
  contract, the freeze, sign-off rows reset to "—".
- `docs/architecture/AB-START-HERE.md`: new state + the v5 runbook (deploy both at the tags, the two smoke gates,
  the two rounds); `CLAUDE.md` "Current state": one concise v5 entry.

## Part D — verification and hand-off

1. `cd backend && dotnet build Forum.slnx` (no new warnings), `dotnet format Forum.slnx --verify-no-changes`,
   new unit tests, then **one** full `dotnet test Forum.slnx` (Docker/Testcontainers; 0 skipped expected).
   Required new tests: cache keys and the §3 invalidation table; integration — feed shows a new thread right
   after create on the same instance; a moderator's cached private search or feed never reaches an anonymous
   reader (gate before cache); disabled switch = no lookups; search `newest` keyset paging without duplicates;
   relay wake-up latency.
2. `cd frontend && npm run typecheck && npm run lint && npm test && npm run build`.
3. `k6 inspect load/k6/main.js` (or a 1-VU dry run against the local compose stack).
4. Live, only if the cluster can be started (`minikube -p forum status`; start it, do not recreate it — the MinIO
   images are no longer pullable): `make mk-deploy`, `ab_check_profile a`, then A-only smokes
   `scripts/ab-round.sh --only a --plan smoke:1 --label v5-a-dry --read-cache off` and the same with `on`.
   Report cache hit ratios, A's WebSocket receipt latency (expect tens of ms instead of seconds) and anything
   that failed. B is still on its old image until Hubert tags `thesis-v5-b` — do not judge B here.
5. Final message in Polish: what changed (by A1–A5, B1–B7, C), test counts, live numbers, open questions, the
   staged file list, a suggested commit message, and the reminder that Jakub tags `thesis-v5-a` after his
   commit.
