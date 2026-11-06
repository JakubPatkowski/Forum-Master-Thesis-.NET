# A/B status and plan — 2026-10-23

**Purpose.** One place that answers, as of Friday 2026-10-23: what is built on both sides, how comparable the two
systems are (and what a reviewer will attack), how we test, which tests and measurements are still missing, what
remains to unify, and the step-by-step plan for (a) the results chapter due for the thesis-seminar class on
2026-10-25, (b) the full measured comparison, (c) the thesis and the article. Written from the code of both
repositories (A `forum-dotnet` @ `8b6a001`, B `gomx` `feature/master-thesis` @ `5c569f7` + 3 uncommitted files),
the archived results and the A/B documents. The Polish presentation of the same content for the owner is the
"Mapa drogowa A/B" artifact of the same date.

Entry point for everything A/B stays `AB-START-HERE.md`; this file is its 2026-10-23 status snapshot + plan.

## 0. Summary

- Both applications are **ready for the final measurement**: the shared functional core of `docs/specs/forum-spec.md`
  §3 exists on both sides and is the only measured surface (journeys J1–J9 + nav, harness h4 / mix v2).
- Everything is committed and pushed **except three gomx files** left out of commit `5c569f7`:
  `app/forum/web/handlers_community_test.go` (the regression test for the anonymous-membership N+1 fix),
  `app/forum/web/web_test.go` (`newTestServerWithStore`), `deploy/k8s/bench/README.md` (D4 note). Commit + push them.
- The "two different applications" objection is answerable (spec-pinned core, core-only measurement, F1–F8
  invariants verified per run, deviation register D1–D14 with bias direction) once three open items are closed:
  Hubert's sign-off of the spec, a "real SPA client" variant for D1, and a decision on B's full-body cards (D2) —
  plus the B WebSocket comment-broadcast leak found today (§2.3, item 6).
- The class deliverable can be written from **round v1** today (real, valid data, N = 5 demo); 10 figures with
  Polish captions and units were generated into `thesis/figures/v1/` (`scripts/thesis_figures.py`). If the v2
  round (`demo:10,stress:3,capacity:3`, ≈ 6 h unattended) runs tonight, the numbers are swapped in the morning.
- What is missing for the full comparison is **measurements, not code**: v2 round, variants (auth-reads, HPA, GC,
  relay 500 ms), soak, spike, chaos ×3, quiet Lighthouse, static metrics on tagged commits, B dev-loop bench.
- The cluster is **stopped** (host reboot); in-cluster images carry `-dirty` tags from the pre-commit trees
  (same code as the commits) — rebuild from the clean HEADs for provenance before the round.

## 0b. Implemented on 2026-10-24 (both trees, uncommitted — owner commits A on `31-feat-phase-13---ab-benchmark`, B on `feature/master-thesis`)

The owner asked for every recommendation of §2.3/§4/§5 that code can close to be implemented on both sides so
that the final rounds run on comparable applications. Done, verified, and summarized for the measurement in
`thesis/results/AB-COMPARISON-v1.md` §5.8:

| # | Item (from §2.3 / §4 / §5) | What landed | Verified by |
|---|---|---|---|
| 2 | D1 idealized SPA client | `REAL_SPA=1` adapter variant (`ab-round.sh --real-spa`), derived cold-cache call set per page in `load/k6/targets/README.md`, recorded as `variant.real_spa`; harness **h5-2026-10-24** | local smokes: default 2.0 vs real 11.8 requests/journey, 0 % failed |
| 3 | D2 B full-body cards | **both** sides ship a 300-char plain-text excerpt: A `forum_content.thread_excerpt()` after the keyset LIMIT (`ThreadFeedItemResponse.Excerpt`, `ThreadCard`), B `GOMX_CARD_PREVIEW_CHARS=300` in the bench overlay (`previewText`, same recipe; product default = full bodies; thread page always full); `ab_check_profile` expects the new B env | A build/format/tests; B `TestListCardsFollowThePreviewPolicy`, `TestPreviewTextStripsMarkdownAndCutsAtRunes` |
| 4 | A posture: D10 live ACL per write | migration `OptimizeEffectiveMask`: one sargable pass over `acl_entries` + cache fast path in `has_permission` (algebra unchanged, 0 mismatches over 3 300 pairs; live −14 %, cached 5.5×) | `AclSqlTests` +2 (cache hit + revocation, live fallback incl. role-principal entries) |
| 6 | B WS leak | `broadcastComment` gates private communities to active members (`commentAudience`) | `TestPrivateCommunityCommentsReachMembersOnly` (member receives, outsider + anonymous do not) |
| 7 | B hub slow consumers | per-socket queue 32 → 256, close-on-full kept | hub tests |
| B | 499 on client cancel | `renderStatus` + `RequestLogger` record 499 instead of 500 | `TestRenderStatusRecordsClientClosedRequest` |
| D6 | A Benchmark social seed | `BenchSocialGraph` mirrors gomx: 2 400 + 80 friendships, 800 conversations, 2 000 DMs, 200 read markers, 80 notifications | `SeedGeneratorTests` +6, `SeedFlowBenchmarkTests` (Benchmark e2e, 10 s) |
| A | tag get-or-create race (found by the h5 smoke) | `INSERT … ON CONFLICT (slug) DO NOTHING` in `TagRepository.GetOrCreateAsync` | `Concurrent_thread_creates_sharing_a_brand_new_tag_all_succeed` |
| A | mobile bugs (TopNav 532 px, tap targets < 40 px) | CSS/markup fixes under `(pointer: coarse), (max-width: 480px)`; `test.fail` markers removed | Playwright 24 passed / 0 failed / 8 skipped (desktop + Pixel 7) |
| A | frontend CVEs | next 15.5.28, overrides sharp 0.35.5 / nanoid 3.3.20 / postcss 8.5.31 / source-map-js 1.2.2 | typecheck, lint, 71 Vitest, build |
| A | stale UI markers / docs | tag-picker SOON badge and "messaging is a preview" tooltip removed; `inbox_messages` name in the domain doc; Social = 38 endpoints in CLAUDE.md; social-seed sentences updated | — |
| spec | §9/§10 | `forum-spec.md` §9 rewritten to harness h5 / mix v2, OQ-1…OQ-7 decided (sign-off table still blank) | — |
| B | gates | gofmt, `templ fmt`, `go vet`, `go test -race ./...` (all ok), golangci-lint 1.64.5 (0 findings), generated code up to date — replayed in the CI image | `scratchpad/gomx-ci.log` |
| A | gates | build 0 warnings, `dotnet format --verify-no-changes` clean, full `dotnet test` **259 passed / 0 failed / 0 skipped** (Content 26, Social 25, Engagement 24, Files 42, Api 59, Architecture 2, Identity 33, Integration 48 — quiet-machine run; one earlier full run that overlapped a `docker compose down` hung for 42 min and failed one Social flow test, which passes alone and in the quiet re-run) · frontend typecheck, lint, 71 Vitest, build, Playwright 24/0/8 | `scratchpad/a-gates2.log`, `integration-rerun.log` |

Not done (unchanged from §4/§6): the measured rounds themselves (v2, `--real-spa`, variants, chaos ×3, quiet
Lighthouse, static re-run on tags, B dev-loop bench), Hubert's sign-off, the HAR-based warm-cache replay, the B
CI lane change (app e2e on Postgres), Tauri runtime passes, README screenshots/LICENSE.

## 1. State (verified 2026-10-23 ~23:00)

| Item | State | Detail | Action |
|---|---|---|---|
| A repo | clean, pushed | `31-feat-phase-13---ab-benchmark` @ `8b6a001`, 8 commits ahead of `origin/master` (PR #30). Branch slimmed today: 345 raw result files (logs, Lighthouse HTML, patches, CSV) dropped from git and gitignored; 493 result files tracked; raw data still on disk (847 files, 109 MB) and in `~/backups/forum-dotnet-2026-10-23/` (git bundle + tar) | keep the backup; plan a public raw-data archive (release/Zenodo) for the thesis |
| B repo | 3 files uncommitted | `feature/master-thesis` @ `5c569f7` pushed (`seedbench.go` Zipf bank + `handlers_community.go` fix) | commit + push the 3 files (§6 step 1) |
| Hubert's `master` | drifted | `e4b1d8d`, 10 commits not on the thesis branch (site/portal, hx-boost moved to the forum shell, per-app security policy, trusted-IP rate limits, mobile offline page); merge conflicts in 4 files | do not merge before the final round; tag the thesis commits on both sides |
| Cluster `forum` | stopped | images: A `forum-dotnet-api/web:git-57f63d0-dirty`, B `gomx:bench-f03d46a-dirty` | `minikube -p forum start`, rebuild both images from clean HEADs |
| Round v1 | archived | `thesis/results/rounds/20261018-0051-v1/` — 14/14 valid, REPORT.md with Welch / Mann-Whitney / Cliff / Holm | source for the class chapter |
| Harness h4 / mix v2 | ready, unmeasured | J4 (+image) and J9 on both adapters, J8 receipt probes, 300-word Zipf bank in both seeders, `AUTH_READS`, soak/spike, round variants, chaos, cluster Lighthouse; dry run `20261018-2349-h4-dryrun` valid | run v2 |
| Chaos | 1 repeat per side (validation) | pod-crash 0 failed on both; DB restart A 4 991 failed / 82 s, B 2 373 / 71 s | repeat ≥ 3× per fault per side |
| Cluster Lighthouse | validation conditions | mobile feed perf A 0.56 vs B 0.89 (TBT 776 vs 34 ms); desktop 0.97 vs 1.00 | re-run on a quiet box after the rebuild |
| Static (H3) | 2026-10-17 | trivy A 13 fixable HIGH/CRITICAL (frontend toolchain) vs B 0; cloc core 52 k vs 30 k; images 179 + 243 vs 41 MB | re-run same day on tagged commits; A toolchain CVE bumps first |
| Figures | new today | `thesis/figures/v1/` (10 PNG + SVG, README with captions + sources), `scripts/thesis_figures.py` | re-run on the v2 round directory |
| Functional gates | green 2026-10-19 | A build 0 warnings, format, 248 backend tests (46 integration), 71 Vitest, 24 Playwright (2 expected mobile failures); B gofmt/vet/tests in the CI image | re-run on the final commits |
| `forum-spec.md` | DRAFT, unsigned | §9 predates harness v1/h4; OQ-1…OQ-7 open | Hubert reviews; rewrite §9 to h4; both sign |
| Thesis material | drafts only | `THESIS-REVISED-SECTIONS-DRAFT.md` (EN drafts of §1.3, §3.2–3.7), `AB-THESIS-GAP-ANALYSIS.md`; no results chapter, no manuscript in the repo | §7, §8 |

## 2. Comparability

### 2.1 Stack, layer by layer (from today's code inventory)

| Layer | A — forum-dotnet | B — gomx | Nature |
|---|---|---|---|
| Language / runtime | C# / .NET 10 + TypeScript (React 19, Next.js 15 as CSR shell, TanStack Query) | Go 1.26.1 + templ; TypeScript for small bundles (Bun 1.3) | paradigm |
| Rendering / API | SPA + JSON API: 84 `/api` routes (81 module endpoints: Identity 12, Content 20, Files 6, Engagement 5, Social 38; 18 anonymous, 63 authenticated) + realtime ticket/WS + vitals beacon; OpenAPI | SSR + HTMX (`hx-boost` in `templdesign/layouts/base.templ` on the thesis branch): 68 forum routes (39 GET, 29 POST; 12 fragment-only), mounted under `/` and `/pl`; no JSON content API; works without JS | paradigm |
| Backend structure | modular monolith, 5 modules with own schemas, CQRS without MediatR, Result pattern, 11 ADRs, architecture tests | one Go module, feature packages, one `shared.Store` port with in-memory + Postgres adapters | A posture (deliberately over-engineered) |
| Data access / model | EF Core writes + raw ADO over SQL views; 37 tables in 6 schemas (27 domain + 10 outbox/inbox), 13 views, 14 migrations | sqlc (96 queries) + pgx; 14 app tables + River tables, 21 goose migrations | paradigm + A posture (audit, soft-delete, outbox) |
| Database | PostgreSQL 18.4 Debian, same args/limits in the bench profile | same | equalized |
| IDs / paging | ULID; keyset everywhere | BIGINT identity; keyset feed, OFFSET search; thread comments unpaginated | paradigm (D-3/D-4) |
| Search | tsvector + GIN, `websearch_to_tsquery`, `ts_rank` | `ILIKE '%q%'` + pg_trgm GIN over posts/users/communities | paradigm; same selective bank since h4 (D4 closed) |
| Auth | JWT 15 min in memory + rotating refresh cookie with reuse detection; Argon2id | server session cookie 30 d (token stored unhashed); PBKDF2-SHA256 100k; OAuth Google/Facebook; email verification sent, not enforced | paradigm; logins outside every window (D-10) |
| Authorization | global roles + bitmask ACL in SQL (`effective_mask`), admin endpoints | per-community roles (owner/moderator/member), join approval; no global admin | A posture (D10 ≈ 1.9 ms SQL per write) |
| Async | transactional outbox → RabbitMQ (exchange per module, inbox dedupe), relay poll 5 s | River (Postgres job queue: media, email) + Redis pub/sub (WS fan-out, presence) | paradigm (D-5, D14) |
| Live updates | WS with single-use ticket, per-view subscriptions, authorization per push, fetch-then-patch | WS with cookie at upgrade (anonymous sockets allowed), broadcast to every socket, client filters; HTML fragments EN+PL; writes over the socket; slow client (32-message buffer) closed with 1008 | paradigm (D-7) + leak (§2.3 item 6) |
| Uploads | presigned PUT to MinIO + commit; images ≤ 5 MiB; magic bytes; dimensions | multipart through the pod → 202 + River job + 1-s status polls; images/audio 5 MB, video 50 MB; `?w=` thumbnails, 64 MB LRU; blob id = SHA-256 | paradigm (D-9, D12), measured by J4 since h4 |
| Observability | Serilog JSON → Loki, OTel → Tempo, Prometheus + exemplars, 8 dashboards, 15 alerts | slog JSON, OTel, Prometheus :9090, GlitchTip, 6 alert rules | equalized for runs (tracing off on both) |
| i18n / SEO | EN only; none (CSR) | EN + PL; robots + sitemap | paradigm; out of scope |
| Desktop / mobile | Tauri v2 desktop (hosted SPA); no Android; Windows runtime pass owed | Tauri v2 desktop (Go server sidecar, offline); generated Android project (`com.gomx.app`) + iOS config | paradigm + title gap (device passes owed on both) |
| Tests | 248 backend (46 integration on Testcontainers PG/RMQ/MinIO, 2 architecture), 71 Vitest, 24 Playwright (desktop + Pixel 7) | 260 Go test functions in 80 files (store suite on Postgres in the MR lane), 129 Playwright (35 templbook specs with axe; 10 app e2e on the in-memory store), Lighthouse CI | complementary cultures |
| CI | GitHub Actions: build-test, frontend, images; weekly CVE + trivy | GitLab: 17 jobs / 7 stages (lint, test, Postgres/Redis lanes, govulncheck, trivy, Sonar, sidecar, Playwright, Lighthouse, package) | descriptive only |
| Size | core 52 k LOC; images 179 + 243 MB | 30 k LOC; 41 MB | H3 |

### 2.2 Shared core (measured) and one-sided breadth

| Core function (spec §3) | A | B | Measured by |
|---|---|---|---|
| register / login, non-revealing | JWT + refresh | session (+ OAuth) | k6 setup only |
| public/private spaces, list, private hidden from outsiders | categories + ACL (list + slug hide private since h4) | communities + membership | nav, J1 |
| threads: markdown, edit, delete, pin | yes (tombstone) | yes (cascade) | J2, J4 |
| nested comments, depth ≤ 5 | materialized path | parent_id | J2, J3 |
| one-click idempotent reaction | like | upvote (+ downvote) | J5 |
| full-text thread search | tsvector | ILIKE + trigram | J6 |
| profile with stats and karma | `user_stats_v` | `AuthorStats` | J7 |
| friends | yes | yes | seed only (B 2 400 pairs; A 0 rows — D6) |
| 1:1 DMs | unified conversations (+ group chat) | 1:1 (replies, share, receipts, voice) | J9 (h4) |
| durable notifications | 5 kinds | 2 kinds | not measured |
| image upload + delivery | presigned | server ingest | J4 (h4) |
| live updates after a write | subscriptions | broadcast | J8 + receipt probes (h4) |

A-only: tags, groups (15 endpoints), blocks, privacy audiences, presence heartbeat, global RBAC/ACL/admin, audit
columns, soft-delete, outbox with inbox dedupe, OpenAPI, 8 dashboards, architecture tests.
B-only: OAuth, email verification, i18n EN/PL, SEO, no-JS path, voice notes, video, WebP thumbnails, DM typing /
receipts / replies / post sharing, review comments with rating, feed time filter, writes over WS, client-error
beacons, GlitchTip, Lighthouse CI, in-memory store mode.

### 2.3 What a reviewer will attack, and how each item closes

| # | Objection | Weight | State | Closing action | Owner · effort |
|---|---|---|---|---|---|
| 1 | the shared spec is an unsigned one-side draft | high | DRAFT (2026-07-18), §9 stale | Hubert reviews + signs; rewrite §9 to h4 (profiles incl. soak/spike, mix v2, ABBA + reseed, scenario/idle windows, J8 probes, variants, statistics); close OQ-1…OQ-7 in one line each | both · 1–2 h |
| 2 | A's harness client is idealized (3 calls per thread vs ≈ 8 + N; no icons/avatars/cache) → A's server cost understated (D1) | high | disclosed as a lower bound, quantified | `REAL_SPA=1` variant in `targets/a.js` issuing the browser's call set (feed + batch + slug→id + icons + avatars; thread + author stats + attachments + related), one demo:3 round → "A ideal / A real / B"; fuller: HAR replay from Playwright; cluster Lighthouse numbers as the client-side truth | Jakub · ½ day (variant) / 1–2 days (HAR) |
| 3 | B's feed/search cards carry whole bodies (174 KB HTML, 20 markdown renders per page) → B penalized for a product choice (D2/D11) | medium | disclosed, decision open | Hubert adds a flag (e.g. `GOMX_CARD_PREVIEW_CHARS=300`), one demo:3 round; report both | Hubert · 1–2 h + 1.2 h run |
| 4 | A is deliberately over-engineered (ACL in SQL per write, outbox + broker, 5 modules, audit) → H3 and part of H1 measure posture (D9, D10, D14, standing cost) | medium | separated in reports: total vs marginal CPU, core vs whole LOC | name it in the thesis (title = "decoupled tiered" as the enterprise default posture); sensitivity variants `--relay-poll-ms 500`, `--gc-variant`; optional D10 (`effective_perm_cache` read in `has_permission`, ½ day + tests) | Jakub · 2.5 h runs; D10 optional |
| 5 | stress/capacity N = 1, demo N = 5 | medium | — | v2 round `demo:10,stress:3,capacity:3`; agree N for hypothesis tables with the supervisor (proposal 10) | Jakub · one night |
| 6 | B broadcasts every comment to every socket without a visibility check → less work per push than A **and a data leak** | medium | **confirmed in code today:** `app/forum/web/live.go` — `broadcastPost` skips private communities, `broadcastComment` does not; the rendered comment HTML of a private-community thread reaches every socket (anonymous included); the client merely does not display it | Hubert: same gate as `broadcastPost` (or deliver to members only) + test; then one demo:3 round (B's per-push cost rises slightly); D-7 stays as the paradigm difference | Hubert · 1–2 h |
| 7 | B's hub closes slow consumers under load (148/152 socket errors in stress/capacity) → J8 on B is lossy | medium | measured; h4 probes count received/late/lost | Hubert decides keep (documented policy) or change; v2 measures loss either way | Hubert · decision |
| 8 | different KDFs / session models | low | logins outside windows (D-10) | one sentence in threats | — |
| 9 | one host, one node, k6 on the same host | low | disclosed; k6 CPU/RSS in every report (≤ 1.27 cores) | relative claims only; watch WSL memory in capacity (k6 reached 2.3 GiB RSS at 300 VUs) | — |
| 10 | each author built one side; both AI-assisted | low | disclosed in §3.7 draft | cross-review against the spec (the signature is the evidence) | both · at sign-off |
| 11 | the capacity knee is the 1-CPU Postgres | low | measured and named | optional sensitivity: one capacity run with a 2-CPU Postgres on both (≈ 1 h) | Jakub · 1 h |
| 12 | B's branch drifts from `master` | low | image from the thesis branch | tag thesis commits on both sides; cite tags + image digests; merge after the final round | both · 5 min |

Verdict: after items 1, 2, 3 and 6 (about one day of work across both sides plus three short rounds) the
objection reduces to what the thesis claims anyway — two paradigms compared on a full system, everything else
equalized, with an explicit deviation register.

## 3. How we test (and what each layer proves)

| Layer | A | B | Proves |
|---|---|---|---|
| Functional gates | build 0 warnings · format · 248 tests (module unit, 2 architecture, 46 integration end-to-end REST → outbox → relay → consumer → WS on real PG/RMQ/MinIO) · typecheck, lint, 71 Vitest, build | gofmt, templ fmt, vet, golangci-lint · `go test -race` 260 functions (store suite on in-memory + Postgres, Redis fan-out) · lint:ts | the measured version is correct; archived per campaign |
| Browser E2E | Playwright 24 executions (desktop + Pixel 7): anonymous home with a private category (regression proven on the pre-fix build), browse, auth + compose, mobile layout (2 expected failures) | Playwright 129 tests: 35 templbook specs with axe, 10 app e2e on the in-memory store | the UI works against the real API; mobile emulated on both |
| Live-cluster checks | every harness endpoint through ingress + TLS, response sizes, posture (private → 404, OAuth mock off, WS ticket), health, Prometheus targets, SQL per journey (`pg_stat_statements`), screenshots | | same work per journey (F1), nothing leaks |
| Closed-model load (H1) | demo 0→10→40→80 VU / 7 min + 20 WS; stress 50→100→150 / 6.5 min + 40 WS; mix v2 J1 30 (30 % page 2), J2 20, J5 12, J6 11, J3 10, nav 8, J9 4, J4 3, J7 2; think 0.3–0.7 s; anonymous reads (`--auth-reads` variant); writes by 200 seeded users | | latency + resources per journey at equal load |
| Open-model load (H2) | capacity 25→500 journeys/s in 8 × 60 s steps, ≤ 300 VUs; knee = p95 > 500 ms, failures > 1 % or achieved < 95 % | | throughput ceiling and its cause (per-layer CPU per step) |
| Live (J8) | parallel WS scenario, 120 s holds with fresh ticket/cookie; every 30 s a probe comment → time to its own push; received / late (> 15 s) / lost | | receipt latency + loss (first numbers: A median 0.2–1.5 s via the 5-s outbox relay, B ≈ 5 ms) |
| Stability / bursts | soak (40 VU × 60 min, A re-login on 401), spike (10↔150 VU ×2, recovery p95 vs baseline) | | leaks, background loops, burst recovery — not run yet |
| Resilience | `ab-chaos.sh`: under demo load kill an app pod (150 s) and restart Postgres (330 s); 5-s timeline, failed requests, recovery time, probe loss | | outbox + broker vs Redis pub/sub + River under faults |
| Client side | cluster Lighthouse (mobile: Moto G class, slow-4G, 4× CPU; desktop), feed + thread, 3 runs, median; RUM Web Vitals beacons on both | | SPA hydration cost vs SSR (TBT, LCP, CLS), accessibility |
| Static (H3) | trivy (same binary, same day), cloc (core vs whole), image/asset sizes, dependency counts, Sonar (configured, not run), dev-loop bench (A `make reload-bench`, B `task reload-bench`; needs Go/bun on the same host) | | dependency surface, CVEs, size, DX |
| Statistics | repeat = experimental unit; mean ± 95 % CI; Welch t, exact Mann-Whitney U, Cliff's δ, Holm (`ab_stats.py`, in every REPORT.md); n < 3 → no verdict | | differences and non-differences are computed, not eyeballed |

## 4. Gaps: tests to add, measurements to run

**A (code).** Playwright in CI (compose + Development seed) or at least a Make target · fix the two pinned mobile
bugs (signed-in TopNav ≈ 532 px on a 412 px phone; tap targets < 40 px) and flip the `test.fail`s · Benchmark
social seed (A-11: friendship ring 1/7/40 + 2 000 DMs in 500 conversations, assertions in `SeedFlowTests`) ·
`REAL_SPA=1` adapter variant (+ smoke) · frontend toolchain CVE bumps (next ≥ 15.5.24, sharp 0.35.5, nanoid,
postcss, source-map-js) then trivy · if D10 is done: unit + integration tests ("role revoked → next write 403").

**B (code, Hubert).** Visibility gate in `broadcastComment` + test (private-community comment never reaches a
non-member or an anonymous socket) · 499 instead of 500 on client cancellation + test · slow-consumer policy
decision + test · app e2e on the Postgres store (CI runs the 10 app e2e on in-memory only) · OQ-3 decision
(anonymous sockets) · optional card-preview flag · commit the three 2026-10-18 files and review `seedbench.go`.

**Measurements.** v2 round `demo:10,stress:3,capacity:3` (≈ 6 h) · variants, one labelled round each:
`--auth-reads` demo:5 (≈ 2 h), `--hpa` demo:5 (≈ 2 h), `--gc-variant` demo:3 (≈ 1.2 h), `--relay-poll-ms 500`
demo:3 (≈ 1.2 h) · soak 60 min per side · spike ×3 · chaos 2 faults × 3 repeats × 2 sides (≈ 2.5 h) · cluster
Lighthouse mobile + desktop on a quiet box (15 min) · static metrics same day on tagged commits (trivy, cloc, sizes,
dependencies, Sonar) · B dev-loop bench on this host (Go + bun + task user-level install, ≈ 30 min, no sudo) ·
optional: capacity with a 2-CPU Postgres, `REAL_SPA` variant round, B preview-flag round.

**Housekeeping found today.** A README badges say 238 backend / 40 frontend tests (actual ≈ 248 / 71), "13 chains"
(14 migrations), k6 section lists only smoke/demo/stress, 10 `[PLACEHOLDER]` screenshots, TODOs for the overview
SVG and Codecov badge, no LICENSE; frontend: stale "SOON" badge on the tag picker (suggestions are real), stale
"Messaging is a preview" tooltip on the thread page, "FILTERS · SOON" in search is a real gap, `/settings` missing
from the frontend README; `DOMAIN-MODEL-AND-DATABASE.md` §7 calls the inbox table `processed_event_ids` (it is
`inbox_messages`); CLAUDE.md says Social = 26 endpoints (38). gomx: `docs/MOBILE.md` says the Android project is
not generated (it is); vote-count comments in `postgres.go` / migration 00001 predate the trigger counters (00019).

## 5. Unification decisions

| Topic | Today | Recommendation | Owner · cost |
|---|---|---|---|
| `forum-spec.md` | DRAFT, §9 stale | rewrite §9 to h4, close OQ-1…OQ-7, both sign | both · 1–2 h |
| Social seed | B 2 400 friendships + 80 pending + 2 000 DMs; A 0 | add the same ring + DMs to A's Benchmark seed (D6 disappears) | Jakub · 2–3 h |
| B card previews (D2/D11) | full bodies | flag + variant round; report both; no forced product change | Hubert · 1–2 h |
| A harness client (D1) | idealized | "real" variant beside the ideal one; ideal stays the main table | Jakub · ½ day |
| Anonymous reads (D3) | main round anonymous | keep; measure `--auth-reads` | Jakub · 2 h runs |
| Outbox relay 5 s (D14) | A receipt 0.2–5 s, B ms | keep 5 s as default (the at-least-once price); measure 500 ms | Jakub · 1.2 h runs |
| GC policy (D8) | A Server GC, B GOGC=100 | keep defaults; `--gc-variant` as sensitivity | Jakub · 1.2 h runs |
| HPA | fixed 2 replicas | keep; `--hpa` (1–3 @ 70 % both) as a scaling class | Jakub · 2 h runs |
| WS broadcast vs subscriptions (D-7) | B broadcasts; leak | Hubert fixes the leak; broadcast stays; OQ-3 decided in the spec | Hubert · 1–2 h |
| Hub slow-consumer policy | closes the socket | Hubert decides; v2 measures loss | Hubert · decision |
| Comment deletion (OQ-1) | A tombstone, B cascade | keep, record | — |
| KDF, sessions, ULID vs int, keyset vs OFFSET, broker vs pub/sub, presigned vs ingest, offline desktop vs thin client | different | keep: these are results; one discussion paragraph each | — |
| Branches and images | `-dirty` tags, B master drifted | rebuild from clean HEADs, tag `thesis-v2-a` / `thesis-v2-b`, merge master only after the final round | both · 20 min |
| Cross-platform (title) | desktop: both Tauri shells; mobile: B Android project, A none | A Windows runtime pass (`frontend/src-tauri/README.md`), B Android pass; thesis: desktop measured, mobile = feasibility path + Lighthouse/Playwright mobile evidence | both · ½ day each |

## 6. Plan with commands

Always start a round with `setsid nohup … &` (a Bash-tool background task dies with the Claude session).
Helper: `abx() { bash -c "source scripts/lib.sh; load_env; source scripts/lib-bench.sh; $*"; }`.

**Fri 2026-10-23 evening (≈ 45 min + overnight run)**
1. gomx: `cd ~/projects/gomx && git add -A && git commit -m "test(bench): pin anonymous membership lookups; bench README D4 note" && git push`; tags: `git tag thesis-v2-b && git push origin thesis-v2-b`; in forum-dotnet `git tag thesis-v2-a && git push origin thesis-v2-a`.
2. Cluster: `minikube -p forum start`; wait for `kubectl get pods -A` all Running (2–3 min). If MinIO pods fail to pull: `minikube -p forum image load ~/.cache/forum-dotnet/images/minio-images-2025-09.tar`.
3. Rebuild from clean commits: `make mk-deploy ARGS=--seed-benchmark` (A) and `make gomx-deploy ARGS=--reset-db` (B). Verify the bank: A `SELECT count(*) FROM forum_content.threads WHERE search_tsv @@ websearch_to_tsquery('simple','abandon')` ≈ 1 600 and `'zephyr'` ≈ 100; same ±10 % with ILIKE on B's `posts`.
4. Smoke (apply the bench profile first, the production limiter 429s): `abx 'ab_apply_profile a'`; `TARGET=a bash scripts/run-load-test.sh smoke`; `TARGET=b bash scripts/run-load-test.sh smoke` → "all thresholds passed", 0 % failed, probes > 0.
5. Round: `setsid nohup make ab-round ARGS="--plan demo:10,stress:3,capacity:3 --label v2" > /tmp/round.log 2>&1 &` (≈ 6 h; no `make tunnels` / Grafana meanwhile). Progress: `tail -f thesis/results/rounds/*-v2/round.log`.

**Sat 2026-10-24.** `make ab-report ARGS=thesis/results/rounds/<stamp>-v2` if needed; figures:
`python3 scripts/thesis_figures.py --round thesis/results/rounds/<stamp>-v2 --capacity-cpu thesis/results/campaign-20261017-2357/03-round/capacity-per-step-cpu.json --lighthouse thesis/results/lighthouse-cluster-20261018 --chaos thesis/results/chaos --out thesis/figures/v2`
(invalid runs are listed in REPORT.md; top up with `--plan demo:2`). Write the chapter (§7). Afternoon, quiet box:
`make ab-chaos ARGS="--target a --fault pod-crash"` ×3, `--fault db-restart` ×3, same for `--target b` (≈ 2.5 h);
`make lighthouse-cluster ARGS="--target a --preset mobile"` + desktop + target b (15 min).

**Sun 2026-10-25.** Class deliverable (chapter from v1 or v2 — both are real, archived data).

**Week 2026-10-26 … 11-01.** One variant round per evening (`--auth-reads` demo:5 `--label v2-auth`; `--hpa` demo:5;
`--gc-variant` demo:3; `--relay-poll-ms 500` demo:3; `--plan soak:1 --k6-env SOAK_MINUTES=60`; `--plan spike:3`).
Hubert: sign the spec; fix `broadcastComment` + 499; decide slow-consumer / anonymous sockets / previews; review
`seedbench.go` + the N+1 test; after his changes rebuild B, smoke, one demo:3 before/after round. Jakub: A-11 social
seed, `REAL_SPA` variant, the two mobile bugs, CVE bumps, README; full gates + rebuild A. Static metrics same day on
the tags (`make security-scan` both repos, cloc, `make artifact-size`, Sonar); install Go + bun + task in `~/.local`
and run `make reload-bench` vs `task reload-bench` (10 runs each).

**Week 2026-11-02 … 11-08.** Final round on `thesis-final-*` tags if the measured path changed, else v2 is final.
Optional: capacity with a 2-CPU Postgres, HAR replay (D1), desktop 5-min session RSS/CPU of both Tauri shells
(spec §9.5), Windows (A) and Android (B) runtime passes. Copy all REPORT.md tables into `AB-COMPARISON-v1.md`
as "Results v2" and update `AB-START-HERE.md`. From 2026-11-09: writing (§8).

Fallback if the v2 round cannot run tonight: write the class chapter from v1 and state that v2 (N = 10, harness h4)
is in progress and that v1 used the non-selective search bank (D4), which inflates J6 on both sides, B more.

## 7. Results-chapter skeleton (class deliverable; working numbering = chapter 5)

All tables exist in the round's REPORT.md; all figures in `thesis/figures/v1/` (captions + sources in its README).
Each table/figure is introduced by a sentence before it and commented by one after it; units in column headers in
square brackets; every mean carries N and the interval type (95 % CI); stress/capacity are "one run per side,
indicative"; memory numbers cite D8, J6 cites D4, A cites D1.

| Section | Tables (columns · units) | Figures (file) | Source |
|---|---|---|---|
| 5.1 Experimental conditions | 5.1 common profile (item · A · B · enforcement) · 5.2 dataset (entity · A · B · shape) · 5.3 journeys and mix (journey · share [%] · A requests · B requests) · 5.4 profiles (profile · model · VU/time [min] · purpose) | run-procedure diagram (8 steps); both architecture diagrams (A `docs/architecture/diagrams/k8s-cluster-architecture.svg`; B to draw) | `AB-COMPARISON-v1.md` §3–§5 |
| 5.2 Functional gates | 5.5 tests (side · kind · count · passed/failed/skipped · time [s]) | — | `01-functional/functional-summary.json`, §2.1 |
| 5.3 H1 (demo) | 5.6 headline (metric · A · B · B/A · CI; journeys/s, ms, KiB/journey, %) · 5.7 per-journey latency (journey · med/p95/p99 · A [ms] · B [ms] · B/A · CI) · 5.8 resources per journey (ms CPU/journey, cores, MiB) · 5.9 significance (metric · mean A · mean B · Welch p · MW p · Cliff's δ · Holm p · verdict) | 5.1 `latency-per-journey-demo` · 5.2 `cpu-per-journey-by-layer-demo` · 5.3 `per-repeat-values-demo` · 5.4 `memory-by-layer-demo` | round REPORT.md demo + "Statistical tests" |
| 5.4 Near saturation (stress) | 5.10 as 5.6 + per journey (N = 1, indicative) | 5.5a `latency-per-journey-stress` | REPORT.md stress |
| 5.5 H2 (capacity) | 5.11 per step (step · offered [j/s] · achieved A · p95 A [ms] · failed A [%] · achieved B · p95 B · failed B) + knee sentence (A sustains 300, knee 400; B 200, knee 300) | 5.5 `capacity-offered-vs-achieved` · 5.6 `capacity-p95-per-step` · 5.7 `capacity-cpu-per-step` | REPORT.md capacity; `03-round/capacity-per-step-cpu.json` |
| 5.6 Live updates (J8) | 5.12 (subscribed, notifications, errors; from v2 also receipt med/p95 [ms], loss [%]) | — | REPORT.md WS table; h4 dry run; v2 |
| 5.7 Resilience | 5.13 (fault · side · failed [count, %] · recovery [s] · max p95 after [ms] · probe loss [%]) | 5.8 `chaos-faults` | `thesis/results/chaos/README.md` |
| 5.8 Client side | 5.14 cluster Lighthouse (page · preset · perf A/B [0–1] · LCP [ms] · TBT [ms] · CLS · a11y) | 5.9 `lighthouse-cluster` | `lighthouse-cluster-20261018/README.md` |
| 5.9 H3 static | 5.15 (CVE HIGH/CRITICAL [count], core LOC, images [MB], first-load assets [KB gzip], dependencies, tests) | — | `04-static/STATIC-SUMMARY.md`, `static/2026-10-17/` |
| 5.10 Summary and limits | 5.16 "who wins where" (domain · A · B · better · confidence) · 5.17 deviation register (D · what · favours · treatment) | — | campaign review §2/§8; `AB-COMPARISON-v1.md` §6.2, §12 |

## 8. Thesis and article

Thesis (Polish): 1 Introduction (aims, H1–H3 per `THESIS-REVISED-SECTIONS-DRAFT.md` §1.3) · 2 Related work (fix
citation [16]) · 3 Method (§3.2–3.7 drafts exist in English; add harness h4: journeys, probes, variants, chaos) ·
4 Implementation (A from ADRs 0002–0011 + FOUNDATION; B by Hubert) · 5 Results (§7; after v2 with N = 10 and variant
subsections) · 6 Discussion (H1–H3 verdicts; four paradigm findings: outbox seconds + durability vs pub/sub
milliseconds + loss; presigned vs ingest; SPA hydration TBT vs wire bytes; memory + standing cost vs tails; posture
vs paradigm) · 7 Conclusions and future work (DB scale, multi-node, RUM with real users, mobile).

Article (IMRaD, 8–12 pages): question = does a unified SSR monolith match or beat a decoupled SPA + API on
resources per journey, throughput ceiling and maintenance surface for the same forum; method highlights = system-level
comparison, journeys not RPS, closed + open models, idle window (standing vs marginal cost), WS receipt probes,
deviation register with bias direction; results = tables 5.6, 5.8, 5.9, 5.11, 5.15 + figures 5.1, 5.3, 5.5.
Division: Jakub A + harness + method + results + statistics; Hubert B + DX/CI + Lighthouse/a11y + mobile; both
discussion + threats; supervisor: N and venue. Artifacts: both repos at tags, raw-results archive, the scripts.

## 9. Files

`AB-START-HERE.md` (entry point, §8/§8.1 runbook) · this file · `thesis/results/AB-COMPARISON-v1.md` · round
`thesis/results/rounds/20261018-0051-v1/` · `thesis/figures/v1/` + `scripts/thesis_figures.py` · campaign
`thesis/results/campaign-20261017-2357/` · `thesis/results/{chaos,lighthouse-cluster-20261018,static/2026-10-17}/` ·
`AB-TEST-GAPS-PROGRESS.md` · `docs/specs/forum-spec.md` + `PROPOZYCJA-UJEDNOLICENIA-A-B.md` ·
`THESIS-REVISED-SECTIONS-DRAFT.md` + `AB-THESIS-GAP-ANALYSIS.md` · `load/k6/`, `scripts/ab-*.sh`, `scripts/ab_*.py`,
`scripts/lib-bench.sh`, `scripts/lighthouse.sh` · `../gomx/deploy/k8s/bench/` · `~/backups/forum-dotnet-2026-10-23/`.
