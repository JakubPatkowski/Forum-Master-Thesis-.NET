# A/B Current-State Audit — forum-dotnet (A) vs. gomx (B)

_Written 2026-07-18 as part of the comparative-architecture audit for the thesis
"UNIFIED FULL-STACK MONOLITHS VS. DECOUPLED TIERED ARCHITECTURES: A QUANTITATIVE ANALYSIS OF PERFORMANCE AND DEVELOPER EXPERIENCE TRADE-OFFS IN CROSS-PLATFORM APPLICATIONS"._

_Method: full read of B's source (`~/projects/gomx`, last commit `eeca387`, 2026-07-18) plus
spot-checks of A's source against its `CLAUDE.md` claims. Every claim cites a file. B was
examined read-only. This is a static analysis — **no benchmark numbers in this document were
newly measured**; the only quantitative results referenced are A's archived 9c run
(`thesis/results/A/20260716-1643-demo/`)._

Companion documents: `AB-THESIS-GAP-ANALYSIS.md` (paper vs. reality),
`AB-UNIFICATION-MASTER-PLAN.md` (what to do about it), per-side backlogs, and the shared-scope
contract draft `docs/specs/forum-spec.md`.

Verdict legend: **A** / **B** = that side is more mature on the dimension today; **≈** = parity
or not meaningfully comparable; **≠** = deliberately different by paradigm (a finding to
discuss in the paper, not a gap to close).

---

## 0. One-paragraph summaries

**A (`forum-dotnet`)** — React 19 / Next.js 15 SPA (pure CSR; `frontend/package.json`) +
.NET 10 modular monolith (`backend/Directory.Build.props:5`), 5 business modules
(Identity, Content, Files, Engagement, Social) with schema-per-module PostgreSQL 17,
RabbitMQ outbox/relay messaging, WebSocket change-feed hub, MinIO presigned uploads,
RBAC + bitmask ACL resolved in SQL, deep observability (Prometheus + exemplars, Loki, Tempo,
8 dashboards, 15 alerts), deterministic seeding, a 3-profile k6 harness with an archived
benchmark run, and 238 backend + ~40 frontend tests. No desktop/mobile packaging of any kind.

**B (`gomx`)** — Go 1.26 server-rendered forum (Templ + HTMX 2 + Alpine.js, Tailwind v4 +
daisyUI, Bun asset pipeline), packaged as a **Tauri v2 desktop app** with the Go server as a
bundled sidecar (`src-tauri/tauri.conf.json` → `bundle.externalBin: ["bin/server"]`), single
`shared.Store` port with in-memory and Postgres (goose + sqlc) adapters, Redis pub/sub WS
fan-out, MinIO/disk media with on-the-fly thumbnails, cookie sessions + OAuth, EN/PL i18n,
Playwright e2e incl. axe-core accessibility checks, Lighthouse CI, RUM Web Vitals, GlitchTip
error tracking, and a tiered 7-stage GitLab pipeline with binary/bundle/image-size metrics.

Neither system resembles the PDF's "enterprise inventory dashboard" — both are forums with
social features (see `AB-THESIS-GAP-ANALYSIS.md` §3.3).

---

## 1. Functional / domain scope

| Feature | A | B | Notes |
|---|---|---|---|
| Forum spaces | Categories (`forum_content.categories`, slug, visibility, icon) | Communities (`app/forum/shared/models.go:136`, slug, public/private, owner) | Same role; B's communities also carry membership+join-approval (A's private categories gate by ACL instead) |
| Threads/posts | `threads` (markdown, pinned, tags) | `posts` (markdown, pinned, category string, attachments, voice) | ≈ |
| Nested comments | Materialized path, **depth ≤ 5** (`Comment.CreateReply`, CLAUDE.md Phase 2) | ParentID tree, **`MaxCommentDepth = 5`** (`models.go:65`) | Accidentally identical cap — lock it in the spec |
| Comment kinds | one kind; delete → `"[deleted]"` tombstone keeps children | `comment/reply/review` + 0–5 rating (`models.go:54-60`); delete cascades | B's "review" kind is B-only |
| Tags | Yes (`tags`+`thread_tags`, suggest endpoint) | **No** (free-text `category` column on posts) | A-only |
| Reactions | Like toggle, `value smallint` future ±; per-target counts via trigger; karma view | **Signed votes ±1** on posts (`Store.Vote … dir int8`, `models.go:328`) + comment likes; karma = net votes + likes (`models.go:96`) | ≠ shape: B already has downvotes, A deliberately reserved the axis. Spec must pick the benchmark action ("upvote/like a post") that exists on both |
| Full-text search | `tsvector` + GIN, `websearch_to_tsquery` (Content `AddFtsAndViews`) | `ILIKE '%…%'` + **pg_trgm GIN** (`migrations/00014_search_trgm.sql`); searches posts, users, communities | ≈ (different tech, both indexed); B also searches users/communities, A only threads |
| Pagination | **Keyset everywhere** (no OFFSET; CLAUDE.md rule 9) | **limit/offset** (`ListPostsPage`, `models.go:323`) | A more scalable; genuine quality delta — see §15 verdicts |
| Friends | request/accept/decline/unfriend, races closed by `ux_friendships_pair` | request/accept/remove; reverse-request auto-accepts (`models.go:396-409`) | ≈ |
| Friend privacy | Per-kind audience settings (`user_privacy_settings`) | `friend_add_policy` everyone/friends-of-friends/nobody (`models.go:177-194`, migration 00008) | ≈ intent; B's friends-of-friends check (`ShareFriend`) is B-only |
| Peer blocks | `social_blocks`, indistinguishable 403s | **None** | A-only |
| DMs | Unified conversations table, group chat shares infra; keyset history; read markers; tombstone delete | 1:1 only (`direct_messages`); read cursors + peer-visible read watermark (`models.go:429-438`); edit + delete; **shared-post cards**; **voice notes** (MP3 + waveform peaks, `models.go:251`); attachments | B's DM UX is richer (voice, share, receipts); A's is structurally more general (group chat) |
| Group chat | Yes — conversation id == group id (Phase 11) | **No** (communities are forum spaces, not chat rooms) | A-only |
| Notifications | Durable rows, 5 kinds, bell + WS push (`forum_social.notifications`) | Durable rows, 2 kinds (`friend_request`, `community_post`), bell badge (`migrations/00015_notifications.sql`) | ≈ mechanism; A more kinds |
| Presence | Heartbeat table, status computed at read (`user_presence`) | Cross-pod online ref-count + presence deltas over the bus (`web/server.go:58-60`, `presence.go`) + **typing indicators** (`typing.go`) | ≈; typing is B-only |
| Files/media | Presigned direct-to-MinIO (ADR 0008), magic-byte probe, image-only 5 MiB, orphan sweep w/ advisory lock | Server-side multipart ingest (`handlers_media.go`), sniff, **images 5 MB / video 50 MB / audio**, on-the-fly thumbnails + 64 MB LRU (`internal/media/thumbnail.go`, `thumbcache.go`), hourly orphan sweep incl. markdown-body blob scan (`models.go:447`) | ≠ by design — upload path IS a paradigm difference (bytes bypass A's backend; B's server pays for ingest+resize). Keep and measure it |
| Moderation | RBAC roles + `moderate` bit at category/group scope; pin, delete, admin user mgmt | Community owner/moderator remove posts/comments (`Membership.CanModerate`, `models.go:276`); member approve/kick | A far deeper (see §4) |
| Avatars | attach-with-replace via Files | upload + `SetAvatar` (`models.go:300`) | ≈ |
| OAuth login | **None** | Google + Facebook (`routes.go:31-35`, migration 00017) | B-only |
| Email verification | **None** (citext email, no mail-out) | Token flow (`models.go:305-311`, `routeVerifyEmail`) | B-only |
| SEO | n/a (CSR SPA) | `robots.txt` + `sitemap.xml` (`handlers_seo.go`, public-posts-only, `models.go:360`) | ≠ paradigm — SSR gets SEO nearly free; a paper point, not a gap |
| i18n | English only | **EN + PL**, go-i18n, `/pl` route mount + cookie (`web/server.go:118-135`, `internal/locale/locales/active.{en,pl}.toml`) | B-only |
| Themes/density | Dark/light via CSS tokens | Theme + density prefs as cookies, server-rendered (`theme.go`, `density.go`) | ≈ |
| Soft-delete + audit columns | Everywhere on aggregates (CLAUDE.md assumption 1) | **No** — hard DELETE cascades, only `created_at` | A-only; affects storage-shape fairness (see forum-spec §data) |

**Verdict: ≈ with disjoint edges.** The common core (register/login, spaces, threads, nested
comments ≤5, one-click reaction, search, DMs, friends, notifications, uploads, live updates)
is real and benchmarkable. Each side owns ~6 features the other lacks; the benchmark scope
must be pinned to the intersection (done in `docs/specs/forum-spec.md`) with the rest
documented as out-of-scope breadth.

## 2. Data model & migrations

- **A**: PostgreSQL **17** (`compose.yaml:4`, `k8s/postgres/statefulset.yaml:28`), one DB,
  **schema per module** (`forum_identity/authz/content/files/engagement/social`), no
  cross-schema FKs, EF Core migration chain per module + raw-SQL migrations for
  FTS/views/ACL/counters; ULID PKs everywhere (ADR 0006); SQL views for reads; audit
  columns + soft-delete on aggregates.
- **B**: PostgreSQL **18-alpine** in k8s (`deploy/k8s/postgres.yaml`, image line ~50), but
  **16-alpine in CI** (`.gitlab-ci.yml:201`) — an internal inconsistency worth flagging to
  Hubert. Single schema, 17 goose migrations
  (`app/forum/shared/postgres/migrations/00001…00017`), sqlc-generated typed queries
  (`queries.sql` = 737 lines → `postgres/db/`), `BIGINT GENERATED ALWAYS AS IDENTITY` PKs,
  count-at-read vote totals (base + live rows, `00001_init.sql` header comment), FK cascades.
- B additionally keeps a full **in-memory Store adapter** (`app/forum/inmem/`) behind the same
  port — the default no-DB demo mode and the desktop sidecar's store. A has no equivalent
  (and doesn't need one; its desktop story is a thin client — see §13).

**Verdict: A** on schema engineering rigor (isolation, keyset-friendly indexes, audit,
deterministic ULIDs); **B** on data-layer DX (sqlc compile-time-checked SQL is a genuinely
strong pattern and should be *featured* in the paper's DX comparison, not treated as a gap).
**Blocking for the benchmark:** Postgres major version must be unified (recommendation: both
→ 18; see master plan U-1).

## 3. Auth & session model

- **A**: Argon2id (Isopoh; ADR 0007), JWT access 15 m in JS memory + refresh 14 d in httpOnly
  cookie, **rotation + family reuse-detection**, non-revealing login + dummy verify, admin
  block. Stateless request auth (signature check, no DB hit); WS handshake via short-lived
  single-use ticket (ADR 0010).
- **B** (verified, not assumed): **PBKDF2-HMAC-SHA256, 100 000 iterations, 16 B salt, 32 B
  key** — Go stdlib, deliberately dependency-free (`shared/models.go:516-536`); **opaque
  32-byte session tokens stored in Postgres** (`sessions` table, `00001_init.sql`), 30-day
  TTL (`models.go:637`), cookie `forum_session` HttpOnly/SameSite=Lax, Secure via
  `GOMX_SECURE_COOKIES` (`web/session.go:112-151`); constant-time verify incl. dummy hash for
  unknown users (`models.go:630`); per-IP auth rate limit 10/min (`web/server.go:243-261`);
  CSRF origin middleware (`internal/server/csrf.go`); **plus OAuth (Google/Facebook) and
  email-verification tokens** (migration 00017).

**Verdict: ≠ by paradigm and keep it** — stateless tokens vs. DB-backed sessions is exactly
the decoupled-vs-monolith trade the thesis studies (B pays a session lookup per request; A
pays token machinery + refresh complexity). Two fairness caveats for the protocol:
(1) **KDF cost differs** (Argon2id is deliberately more expensive than PBKDF2-100k), so login
throughput is NOT comparable as an architecture signal — both harnesses must keep credential
hashing out of the hot-path percentiles (A's 9c setup already does; B's k6 should too), and
the paper should state the KDF difference. (2) A's security posture (rotation, reuse
detection, Argon2id) is materially stronger; that belongs in the qualitative comparison.

## 4. Authorization

- **A**: global roles + per-context roles, bitmask ACL resolved in SQL
  (`docs/db/permissions-acl-design.md`, ADR 0004; `effective_mask()`, perm cache, BRIN/partial
  indexes), group-admin = `moderate` bit at group scope (Phase 11).
- **B**: three community roles + membership status; `CanModerate` in Go
  (`models.go:276`); no global staff roles, no ACL, no admin surface.

**Verdict: A, by a wide margin.** Do **not** ask B to build an ACL engine — that would be a
multi-week rebuild with no benchmark payoff. Instead the spec pins the *behavioural* contract
(who can delete/pin/approve) and lets each side keep its enforcement mechanism; the
authorization-architecture comparison becomes a paper section (SQL-resolved ACL vs. in-process
role checks is a legitimately interesting A/B contrast).

## 5. Real-time transport

- **A**: writes → transactional outbox → RabbitMQ topic exchanges (publisher confirms,
  `FOR UPDATE SKIP LOCKED` relay) → per-replica exclusive queue → WS hub in Bootstrap →
  JSON change-notification `{type, entity, id, parentId, categoryId}`; client re-fetches and
  patches (ADR 0010/0011). Push-time authorization per event; ticket handshake; 22 events.
- **B**: writes → in-process hub (`hub/hub.go`, 200 lines) with Redis pub/sub cross-pod
  fan-out (`redisbus/redisbus.go`, channel `forum:events`); payload = **server-rendered HTML
  fragment** injected by `forum-live.ts` (`htmx.process` on injected markup, one socket
  surviving hx-boost swaps — documented in `STRUCTURE.md` §Live updates); presence deltas ride
  the same bus; session-cookie auth at upgrade + origin allow-list; DM send happens *over the
  socket* (HTTP POST is the no-JS fallback, `server.go` comment at `routeMessages+"/{userID}"`).
- Durability differs: A's bus is at-least-once with inbox dedupe (consumers have side
  effects); B's is fire-and-forget pub/sub (a dropped fragment self-heals on next
  navigation) — each is the right choice for its payload semantics.

**Verdict: ≠ — this is the single best architectural contrast in the whole project** (JSON
notify-then-refetch vs. rendered-fragment push; broker-with-outbox vs. best-effort pub/sub)
and deserves its own paper subsection plus a WS-focused benchmark scenario (write→client-
receipt latency, fan-out cost per replica). Not a gap on either side.

## 6. Observability

- **A**: Serilog compact JSON → Alloy → Loki; OTel traces → Tempo with **verified exemplars**;
  `ForumMetrics` (12 instruments incl. per-loop tick-age); correlation id across response ↔
  log ↔ trace ↔ outbox; 8 Grafana dashboards + `QUERIES.md`; 15 evaluate-only alerts;
  `OBSERVABILITY-CONTRACT.md` pinned by tests. (Phases 9a/10c, verified live.)
- **B**: Prometheus route-labelled HTTP metrics + domain counters (auth funnel, writes
  rejected, community events, upload size/duration, thumbnail cache hit/miss, WS gauge)
  (`internal/metrics/metrics.go`); slog JSON + RequestID + `trace_id` in request logs
  (`internal/logging/`, `router.go:41-53`); OTel tracing incl. **otelpgx** (Postgres spans)
  and **redisotel**; Loki + Alloy + Tempo manifests (`deploy/monitoring/{loki,tempo,
  alloy-logs}.yaml`) and a local compose observability stack (`deploy/observability/`);
  2 Grafana dashboards (app + live-streamed k6); 6 alert rules (`prometheusrule.yaml`);
  metrics on a non-ingress port 9090 in-cluster; **plus two things A lacks entirely: RUM
  Core Web Vitals beaconed from real browsers (`POST /vitals` → `web_vitals_*` histograms,
  `metrics.go:137-147`) and client-side JS error tracking → GlitchTip (`/clienterror` →
  `internal/errortrack`, Sentry SDK, `deploy/glitchtip/`)**.

**Verdict: A on server-side depth; B on user-side (field) telemetry.** For the paper's Q2
(rendering/network efficiency) B's RUM pipeline is the more relevant capability, and A
currently has **no Core Web Vitals story at all** — closing that (Lighthouse harness at
minimum, RUM beacon ideally) is a blocking A-side item (plan A-2/A-3).

## 7. Load testing & benchmark harness

- **A**: k6 v2.1.0, `load/k6/main.js` — smoke/demo/stress + parallel WS scenario, realistic
  9-action mix, per-endpoint thresholds, zero-429 guard; `bench-run.sh` full runbook
  (preflight, limiter raise/restore, HPA grace, N repeats, Prometheus snapshots, fairness
  checklist) archiving to `thesis/results/A/…` — one demo archive exists (2026-07-16:
  113.5 ± 2.2 req/s, p95 34.5 ± 15.1 ms, 3 repeats).
- **B**: one k6 script (`app/forum/loadtest/forum.ts`) with two scenarios (HTTP readers/
  writers scraping live thread ids + WS holders), thresholds p95<500 ms / fail<1%; results
  stream into Prometheus via remote-write with a `testid` per run and a dedicated Grafana
  dashboard (`README.md` §Streaming k6, `deploy/monitoring/values.yaml`
  `enableRemoteWriteReceiver`); pprof profiling documented (`loadtest/README.md`).

**Verdict: A** on protocol rigor (profiles, repeats, archival, environment pinning) — this is
the harness the thesis results should run on. **B's remote-write→Grafana live view is worth
adopting on A** (nice-to-have). Blocking joint item: one shared scenario mix + dataset +
thresholds (spec §benchmark) so the two k6 scripts measure the same user behaviour.

## 8. Seeding / dataset

- **A**: deterministic ULID seeder, Development + Benchmark profiles (800 users / 12
  categories / 1600 threads / 9000 comments / 15000 reactions, measured 24 MB), idempotency
  guard, k8s seed Jobs (Phase 9b).
- **B**: `shared/seed.go` demo seed (24 posts, several communities, generated images) run
  automatically on empty store/DB (`internal/server/forum.go:78-84`). No volume-scale
  deterministic benchmark seed.

**Verdict: A. Blocking:** B needs a benchmark-scale seed matching the agreed volumes, or the
concurrency-ceiling comparison (Q5/H2) is invalid — B would serve hot caches over 24 rows
while A pages over 10k+. Proposal for Hubert in `AB-UNIFICATION-PLAN-ARCHITECTURE-B.md` (B-1).

## 9. CI/CD

- **A**: GitHub Actions, 2 workflows: `ci.yml` (16 lines — restore/format/build/test,
  **backend only; the frontend is not in CI at all**) and `security.yml` (weekly
  `dotnet list package --vulnerable`). No image build, no e2e, no size metrics in CI.
- **B**: GitLab, 581-line tiered pipeline (`.gitlab-ci.yml`): fast lane lint:go/lint:ts/
  test:go(-race+coverage) on every push; mr-to-master adds Postgres & Redis service
  integration jobs; full lane adds govulncheck, trivy fs (vuln+secret+misconfig), SonarQube
  (token-gated), prod binary + asset builds, **binary/bundle/image-size OpenMetrics reports
  diffed per MR**, Playwright e2e (Chromium/Firefox/WebKit + mobile-safari) via a prebaked
  image, Lighthouse; master packages the container.

**Verdict: B, decisively** — and directly relevant to H3/Q3. A's minimum bar: frontend
typecheck/lint/test/build in CI + an image build; the paper's "identical infrastructure"
CI-timing methodology needs replacement regardless (see gap analysis §3.4/§3.5 — pipelines run
on different SaaS runners and different job scopes; local build benchmarks are the defensible
metric, and B already has a purpose-built one: `task reload-bench`, `scripts/reload-bench.sh`).

## 10. Static analysis, CVE, complexity

- **A**: `.editorconfig` + analyzers + `dotnet format` gate; NuGetAudit + weekly CVE workflow;
  ArchitectureTests enforce module boundaries (a form of static analysis B has no equivalent
  of). No SonarQube, no trivy, no secret scanning.
- **B**: golangci-lint (`.golangci.yml`), ESLint+Prettier, govulncheck (reachability-aware),
  trivy fs, SonarQube config (`sonar-project.properties`, Go coverage wired), Renovate
  (`renovate.json`), Husky hooks.

**Verdict: B.** For the H3 CVE/complexity metrics to be comparable, both repos must be
scanned by the **same tool on the same day** (trivy fs for CVEs; SonarQube — which supports
both Go and C# — for cyclomatic complexity). A-side items A-6/A-7.

## 11. Accessibility & frontend audit

- **A**: none (no axe, no Lighthouse, no Playwright).
- **B**: @axe-core/playwright helper (`templdesign/tests/e2e/support/a11y.ts`) used across
  ~34 component/template e2e specs + 4 app-level specs (`app/forum/web/e2e/{live,media,
  settings,voice}.e2e.pw.ts`); Lighthouse CI with a11y/BP/SEO/perf soft budgets
  (`lighthouserc.json`) both locally and in CI.

**Verdict: B.** Lighthouse on A is blocking for Q2 (it is the paper's stated CWV instrument);
axe/Playwright on A is recommended-but-not-blocking (a11y is not one of the five metric
categories — it strengthens the DX/maintainability narrative).

## 12. i18n

A: English only. B: EN/PL route-and-cookie i18n (go-i18n). **Verdict: B-only; not blocking**
— i18n is outside the five metric categories. Benchmark journeys run EN on both sides; the
paper lists i18n as a B-side scope note. Building i18n into A would be busywork for the
comparison (recorded as explicitly rejected in the master plan).

## 13. Desktop / cross-platform packaging  ⟵ the headline gap

- **A**: **nothing** — no Capacitor/Ionic (contradicting PDF §3.2), no Tauri, no Electron
  (verified: zero matches in `frontend/package.json`).
- **B**: working Tauri v2 desktop shell — Rust host + Go server sidecar
  (`src-tauri/tauri.conf.json`: `externalBin: ["bin/server"]`, `beforeBuildCommand: task
  build-sidecar`), sidecar binaries built in CI (`build:sidecar`), fully offline-capable
  (in-memory store); **plus a decided-and-scaffolded mobile plan** — `MOBILE.md` (Option 1
  hosted shell chosen), `tauri.android.conf.json`/`tauri.ios.conf.json` committed,
  `#[cfg(desktop)]`-gated sidecar in `lib.rs`; platform projects not yet generated (no
  Xcode/SDK on the Linux env).

**Verdict: B; blocking for the title.** The registered (immutable) title promises
"Cross-Platform Applications"; today only B honors it. Minimum viable fix and the shell-choice
argument (Tauri for A too, to hold the shell constant) are in `AB-THESIS-GAP-ANALYSIS.md` §T
and plan item A-1. One asymmetry to embrace rather than hide: **B's desktop app is the whole
stack offline; A's can only ever be a connected thin client** (nobody bundles a .NET API +
Postgres into a desktop installer) — that is a genuine consequence of the decoupled paradigm
and belongs in the results discussion.

## 14. Testing overall

- **A**: 238 backend tests — unit + 2 architecture-enforcement + 38 integration over real
  Testcontainers Postgres/MinIO/RabbitMQ (full REST→outbox→relay→consumer→WS paths); ~40
  frontend Vitest units; missing: Social E2E suite (`SocialFlowTests`, owed), any browser e2e.
- **B**: 57 Go test files (unit incl. domain rules; Postgres adapter integration against a
  real service in CI; Redis fan-out test), ~38 Playwright specs incl. a11y, coverage % gate
  in CI. No architecture-boundary tests (its package graph is enforced only by Go imports).

**Verdict: ≈, complementary shapes.** A is deeper below the HTTP line, B above it. For the
paper this is itself a DX data point (backend-integration-heavy vs. browser-e2e-heavy testing
cultures of the two stacks).

## 15. Summary scorecard

| Dimension | Verdict | Blocking for a valid comparison? |
|---|---|---|
| Functional core | ≈ (disjoint edges) | Yes — pin scope in forum-spec |
| Data model rigor | A | No |
| DB engine version | 17 vs 18 (+16 in B CI) | **Yes — unify (→18)** |
| Auth/session | ≠ paradigm | Only the KDF caveat |
| Authorization depth | A | No (behavioural contract instead) |
| Real-time | ≠ paradigm | No — add joint WS scenario |
| Server observability | A | No |
| RUM / Web Vitals | B | **Yes — A needs Lighthouse (+RUM)** |
| Load-test harness | A | **Yes — B needs profiles/seed; joint mix** |
| Benchmark dataset | A | **Yes — B needs volume seed** |
| CI/CD | B | Partly — A adds frontend CI; CI-timing metric redefined |
| CVE/complexity tooling | B | **Yes — same scanner on both** |
| Accessibility testing | B | No |
| i18n | B | No (excluded from benchmark) |
| **Desktop/cross-platform** | **B** | **Yes — A needs a shell (title)** |
| Tests | ≈ | No |
