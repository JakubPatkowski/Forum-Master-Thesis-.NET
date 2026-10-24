# Forum A/B Shared Functional Specification

> **STATUS: DRAFT — pending Hubert's review and sign-off.** §9 (protocol) and §10 (decisions) rewritten 2026-10-24 to
> the implemented harness h4 / mix v2; the rest of the document is unchanged since 2026-07-18.
> Drafted 2026-07-18 by the A side from a read-only audit of both repositories
> (`forum-dotnet` @ branch `27-feat-phase-11---social`; `gomx` @ `eeca387`). This document is
> cited across `forum-dotnet` (CLAUDE.md, several ADRs) as "the authoritative shared A/B
> contract" and had never actually been written — this draft pays that debt. It becomes
> authoritative only when both authors sign the box at the bottom. Until then, where this
> draft and either implementation disagree, the disagreement is an open question, not a bug.

Related: `../architecture/PROPOZYCJA-UJEDNOLICENIA-A-B.md` (the negotiation companion —
what each side changes), `../architecture/AB-CURRENT-STATE-AUDIT.md` (evidence),
`../architecture/AB-UNIFICATION-MASTER-PLAN.md` (execution).

---

## 1. Purpose and scope

Two independently built forum systems are benchmarked for the thesis
"UNIFIED FULL-STACK MONOLITHS VS. DECOUPLED TIERED ARCHITECTURES: A QUANTITATIVE ANALYSIS OF
PERFORMANCE AND DEVELOPER EXPERIENCE TRADE-OFFS IN CROSS-PLATFORM APPLICATIONS":

- **Architecture A** — `forum-dotnet`: React 19/Next 15 CSR SPA + .NET 10 modular-monolith
  REST API + PostgreSQL + RabbitMQ + MinIO; Tauri v2 desktop shell (thin client).
- **Architecture B** — `gomx`: Go + Templ SSR + HTMX/Alpine + PostgreSQL + Redis + MinIO;
  Tauri v2 desktop shell (embedded server sidecar, offline-capable).

This spec pins (a) the **shared functional core** both systems must implement with
observably equivalent behaviour, (b) the **explicit non-goals** each side keeps as
out-of-benchmark breadth, (c) the **shared dataset**, and (d) the **benchmark protocol**.
It pins *behaviour*, never implementation technique — differing techniques for the same
behaviour are the object of study.

## 2. Concept mapping (canonical vocabulary)

| Canonical term (spec/paper) | A realization | B realization |
|---|---|---|
| **Space** (topic space) | Category (`forum_content.categories`) | Community (`communities`) |
| **Thread** | Thread | Post |
| **Comment** | Comment (materialized path) | Comment (parent-id tree) |
| **Reaction** | Like toggle (`forum_engagement.reactions`) | Post upvote (`post_votes`, dir=+1) / comment like |
| **Space staff** | Category owner, or holder of `moderate` at category scope | Community owner/moderator |
| **Global staff** | `moderator`/`admin` global roles | — (B has no global staff; see §4.7) |
| **Friend request / friendship** | `forum_social.friendships` | `friendships` |
| **DM** | Direct conversation (2 participants) | 1:1 `direct_messages` |
| **Notification** | `forum_social.notifications` | `notifications` |
| **Upload** | Files module (presigned) | media store (server ingest) |

## 3. Shared functional core (benchmark surface)

Everything in this section must exist and behave equivalently in both systems. "Equivalently"
= same user-observable outcome and same authorization verdict; status-code numerology and
payload format are per-paradigm (A: JSON + RFC 7807; B: HTML + redirects/fragments).

### 3.1 Accounts & auth
- Registration with username (unique, case-insensitive) + password; password policy enforced
  server-side (each side keeps its own policy strength; not benchmarked).
- Login; logout. Failed login is non-revealing (no user-exists oracle) — both sides already
  do this (A: dummy verify; B: `VerifyPassword` with zero-value salt, `models.go:630`).
- Session continuity per paradigm: A JWT access+refresh w/ rotation; B server-side cookie
  session. **Credential-hashing endpoints are excluded from benchmark hot paths** (different
  KDFs by design: Argon2id vs PBKDF2-100k).

### 3.2 Spaces
- List/browse spaces; public spaces readable by everyone incl. anonymous.
- Private spaces: content hidden from non-members/non-authorized; membership granted by
  owner approval (B) / ACL grant (A). The benchmark only *reads* private-space authorization
  (403/hidden), it does not measure the grant flow.
- Create space (authenticated); slug-addressed.

### 3.3 Threads & comments
- Create thread (title + Markdown body) in a space; server-side Markdown rendering (B) /
  sanitized client rendering (A) — XSS-safe on both (A: rehype-sanitize; B: bluemonday).
- Edit own thread; delete by author or space staff.
- Nested comments, **max depth 5** (already identical: A `Comment.MaxDepth`, B
  `MaxCommentDepth` — `gomx/app/forum/shared/models.go:65`); reply control disabled/rejected
  below the cap on both.
- Comment edit (own) and delete (own or staff). Deletion visibility may differ (A tombstones
  `"[deleted]"` keeping children; B cascades) — **open question OQ-1** below.
- Pinned threads float above the feed.

### 3.4 Reactions
- One-click reaction on a thread ("like"/"upvote": the canonical benchmark action), per-user,
  idempotent toggle; counts visible on cards; comment likes likewise.
- B's downvote (dir=-1) and A's future signed values are out of benchmark scope.

### 3.5 Feed, search, profiles
- Home feed: newest-first with pinned float, paginated ("load more" — 20/page both sides).
- Space feed: same within one space.
- Full-text search over thread title+body, paginated; anonymous allowed.
- Public profile: display name, avatar, karma, thread/comment counts, recent activity.
  Karma definition (agreed): net reaction sum over the user's live content (A:
  `user_stats_v`; B: `AuthorStats`, `models.go:96` — semantics already match closely enough;
  **OQ-2** on exact formula).

### 3.6 Social
- Friend request → accept/decline; unfriend. Sending to an existing reverse-pending pair
  accepts it (both already do).
- 1:1 DM between friends: send, list conversation history, unread count in the UI shell,
  own-read marker. (A's group chat, B's voice/share-post/read-receipts: out of scope, §5.)
- Durable notifications with unread badge for at minimum: friend request received, friend
  request accepted. (Other kinds allowed, not benchmarked.)

### 3.7 Uploads
- Authenticated image upload (PNG/JPEG/GIF/WebP), max 5 MiB, server-verified type (magic
  bytes, both sides already sniff), attach to thread/comment; delivery to any viewer who can
  see the target. Transport differs by design: A presigned direct-to-MinIO PUT + commit;
  B multipart POST `/uploads`. The **benchmark journey is "attach one ~68-byte PNG to a new
  thread and later fetch it"** on both.

### 3.8 Live updates
- A logged-in client viewing a space/thread receives a push over WebSocket when another user
  creates a thread in that space / comments in that thread / reacts, without polling; UI
  reflects it without full page reload. Delivery semantics: at-most-once acceptable
  (self-healing on navigation); ordering not guaranteed.
- WS auth: A ticket handshake; B cookie at upgrade — per-paradigm, both must reject
  unauthenticated upgrades (B allows anonymous viewers? **OQ-3**).

### 3.9 Operational contract
- `GET` health/readiness endpoints (A `/health/live|ready`; B `/healthz|/readyz`).
- Prometheus metrics NOT publicly routed (both already: A ingress 404; B separate port 9090).
- Rate limiting exists; benchmark-mode values pinned in §9 and recorded per run.

## 4. Out-of-benchmark breadth (kept, documented, not leveled)

| # | Feature | Side | Paper treatment |
|---|---|---|---|
| 4.1 | Tags + tag suggest | A | scope note |
| 4.2 | Group chat, peer blocks, privacy audiences, presence/typing* | A (blocks/groups), B (typing) — presence exists on both | scope note |
| 4.3 | Voice notes, DM shared-post cards, DM read receipts | B | scope note |
| 4.4 | OAuth (Google/Facebook), email verification | B | scope note |
| 4.5 | i18n EN/PL, themes/density prefs | B | scope note (benchmarks run EN) |
| 4.6 | SEO (robots/sitemap) | B | **paper section** — paradigm consequence |
| 4.7 | Global staff roles + bitmask ACL + admin surface + audit columns + soft-delete | A | **paper section** — enterprise-hardening depth as a DX/maintainability discussion |
| 4.8 | Comment kinds (review + rating) | B | scope note |
| 4.9 | Outbox/broker vs pub/sub internals | both | **paper section** — architectural comparison |

## 5. Benchmark journeys (the only measured surface)

J1 anonymous feed browse (home, 2 pages) · J2 thread open (thread + comments + reaction
states) · J3 authenticated comment post · J4 authenticated thread post (with 1 image on x%
of iterations) · J5 reaction toggle · J6 search query + open result · J7 profile view ·
J8 WS hold (subscribed viewer receiving pushes; receipt latency sampled) · J9 DM send
between seeded friends. Weights/think-times: proposal in §9.3 (based on A's 9c mix — J-4
negotiation point).

## 6. Data contract

- Limits (shared): thread title ≤ 200 chars; body ≤ 40 000 chars Markdown; comment ≤ 4 000;
  comment depth ≤ 5; upload ≤ 5 MiB, image types above; page size 20. (**OQ-4**: B's current
  literal limits need verification — A's are enforced in validators.)
- IDs are opaque strings in URLs from the spec's perspective (A ULID, B int64 — disclosed
  difference, not normalized).
- Timestamps UTC.

## 7. Benchmark dataset (deterministic, identical volumes)

Adopted from A's proven Benchmark profile (measured 24 MB, seeds in ~13 s on A):

| Entity | Count | Shape |
|---|---|---|
| Users | 800 | ≥700 "ordinary" bench users with known password; staff/special bands documented per side |
| Spaces | 12 | 8 public + 4 private (each private space: 25 seeded members) |
| Threads | 1 600 | Zipf-distributed across spaces; 1% pinned; body lengths from the shared word-bank distribution |
| Comments | 9 000 | depth 0–4 distribution as in A's seeder |
| Reactions | 15 000 | 75/25 thread/comment, Zipf over targets |
| Friendships | **proposal:** 2 000 accepted pairs + 200 pending | needed for J9; **new for both sides** (A's bench seed currently has 0 social rows — blocked on this spec; B has none) |
| DMs | **proposal:** 5 000 messages across 500 conversations | J9 read paths |

Determinism: fixed seeds; identical counts on re-run; row counts recorded in run metadata.
Content: shared word bank (A can export its seeder word list) so text-length distributions
match.

## 8. Environment & resource budget (per run, both sides)

- Same workstation, same day per paired run set; WSL2 VM ≥ 12 GB; minikube profile: 6 CPUs /
  10 GiB (A's current `.env`) — **OQ-5**: confirm B's stack fits the same budget with its
  monitoring installed, else agree a common budget.
- Kubernetes: same minikube + CNI version; app namespace resource requests/limits recorded.
- HPA: min/max replicas and CPU target pinned per side and recorded (A: 70% target; B: 60%,
  min2/max10 — **OQ-6**: unify targets or record and justify).
- PostgreSQL 18, MinIO pinned versions; broker/redis per side.
- Rate limiters set to documented benchmark values (recorded in meta.json) on both sides.

## 9. Measurement protocol (binding version: harness h4 / mix v2, 2026-10-24)

> Rewritten 2026-10-24 from the proposal of 2026-07-18 to the protocol that is actually implemented and was used
> for the v1 round (`thesis/results/rounds/20261018-0051-v1/`) and that the v2 round uses. The single source of the
> mechanics is `thesis/results/AB-COMPARISON-v1.md` §3–§6 (environment, dataset, harness, invariants F1–F8,
> deviation register); this section pins what both authors agree to measure and how.

### 9.1 Environment and run isolation
One minikube cluster (profile `forum`; 6 CPUs / 10 GiB; calico, ingress-nginx, metrics-server), A in namespace
`forum-dotnet`, B in `gomx`, one shared ingress with TLS for both hosts (`forum.local`, `gomx.local`) and gzip at
the ingress for both. Runs are **sequential**: the idle architecture's app is scaled to 0. The **common profile**
is applied and verified by `scripts/lib-bench.sh` (`ab_check_profile`) before every run and a drift aborts the run:
2 fixed app replicas without HPA, requests 250m/256Mi, limits 1 CPU/512 MiB, probe timeouts 5 s; PostgreSQL
`postgres:18` (18.4 Debian) with `shared_buffers=256MB effective_cache_size=512MB max_connections=100`,
250m/512Mi → 1 CPU/1 GiB; pool 30 per replica; tracing OFF; rate limiters neutralized and recorded (any 429
invalidates the run). B-only flags in its bench overlay: `GOMX_SEED=bench`, `GOMX_COMPRESS=0`,
`GOMX_NOTIFY_FRIENDS_ON_POST=0`, `GOMX_DB_MAX_CONNS=30`, `GOMX_CARD_PREVIEW_CHARS=300` (list cards carry the same
300-character excerpt A's cards carry), OAuth mock off. Only A's monitoring stack runs; B's ServiceMonitor is
applied to it. k6 v2.1.0 runs on the WSL host outside the cluster; its CPU/RSS is sampled and reported.

### 9.2 Journeys and mix v2
One k6 entrypoint (`load/k6/main.js`) with one adapter per side (`targets/a.js`, `targets/b.js`). A journey is one
user-visible action: on A the SPA's API calls, on B one HTML round trip. Weights (closed and open profiles alike):
J1 space feed 30 (30 % also page 2 by cursor) · J2 thread open 20 · J5 reaction toggle 12 · J6 search 11 ·
J3 comment 10 · nav list spaces 8 · J9 direct message 4 · J4 thread with a photo 3 · J7 profile 2; J8 = a parallel
WebSocket scenario (hold 120 s, subscribe one public space on A / cookie socket on B) with a **receipt probe** every
30 s (own comment → time to the matching push; received / late > 15 s / lost). Selection rules are layout-independent
and identical on both sides (space: 50 % one of the three largest public spaces, 50 % uniform; thread: uniform over
the whole public corpus; search word: uniform over the shared 300-word Zipf bank; comment body = `seeded` + 18 bank
words). Reads are anonymous (variant `--auth-reads` sends the session on every read); writes use one of 200 seeded
users per VU, logged in during k6 setup (setup is outside every measured window). Think time 0.3–0.7 s in closed
profiles. A's adapter has an opt-in `REAL_SPA=1` mode that issues the real browser's cold-cache call set per page
(deviation D1); the default remains the idealized client and both are reported.

### 9.3 Profiles
`smoke` (5 VU × 60 s, gate) · `warmup` (10 VU × 60 s, ~6 % writes, discarded before every measured run) ·
`demo` (0→10→40→80 VU over 7 min + 20 WS VUs; H1 at moderate load) · `stress` (50→100→150 VU over 6.5 min + 40 WS;
H1 near saturation, informational thresholds) · `capacity` (open model: 25, 50, 100, 150, 200, 300, 400, 500
journeys/s, 15 s ramp + 45 s hold each, ≤ 300 VUs + 20 WS; H2) · `soak` (40 VU × 60 min; stability) · `spike`
(10↔150 VU twice; recovery p95 vs baseline). Knee (H2) = first step with journey p95 > 500 ms, failures > 1 % or
achieved < 95 % of offered; the sustained throughput is the highest step before it.

### 9.4 Procedure, repeats, statistics
Per measured run: scale the other side to 0 → reseed the target (Benchmark seed) + `ANALYZE` + fresh pods →
verify the profile → warm-up → measured run → idle window 20–50 s after k6 exits (standing cost) → validity checks
(k6 exit, pod restarts, 429s, failure rate, profile check) → cool-down. Repeats are interleaved **ABBA**. Minimum
repeats: demo ≥ 10, stress ≥ 3, capacity ≥ 3 (N for hypothesis-tested tables agreed with the supervisor; 10 is the
working value). The repeat is the experimental unit: mean ± 95 % CI, Welch's t, exact Mann-Whitney U, Cliff's δ and
Holm-adjusted verdicts (`scripts/ab_stats.py`) in every generated `REPORT.md`; n < 3 → no verdict. Variants are
separate, labelled rounds: `--auth-reads` (D3), `--hpa` (identical HPA 1–3 @ 70 % on both, start at 1), `--gc-variant`
(A workstation GC, B `GOMEMLIMIT=384MiB`), `--relay-poll-ms 500` (A outbox relay; D14), `--real-spa` (D1).
Resilience: `scripts/ab-chaos.sh` (pod crash / pod delete / DB restart under demo load, ≥ 3 repeats per fault per
side; failed requests, recovery time, probe loss).

### 9.5 Units and thresholds
Journeys/s over the scenario window (never raw RPS); per-journey median/p95/p99 in ms; failed requests < 1 %;
zero 429; CPU-ms per journey per pod class (app, DB, bus, storage, ingress; total and marginal = minus the idle
window) and MiB working set from Prometheus over the exact window; KiB received per journey on the wire (gzip +
TLS); WebSocket subscriptions, pushes, errors, receipt latency (write start → push) and received/late/lost shares.

### 9.6 Non-load metrics (same instrument, same day, both sides)
Lighthouse on the benchmark deployment through the ingress (`scripts/lighthouse.sh --cluster`), feed + thread,
mobile (Moto G class, slow-4G, 4× CPU) and desktop presets, 3 runs, median; RUM Web Vitals beacons on both; browser
E2E suites (A: Playwright desktop + Pixel 7 emulation; B: Playwright incl. mobile projects). Static: trivy (one
pinned binary, filesystem mode), cloc (core vs whole), container image and first-load asset sizes, dependency counts
(direct from manifests, transitive from lockfiles/module graph), SonarQube (one server, generated code excluded),
dev-loop timings (10 runs each of clean build / incremental build / test wall clock / edit-to-running change, same
machine, same day; A `make reload-bench`, B `task reload-bench`). CI wall clock is reported descriptively only.

### 9.7 Run artifacts
`thesis/results/rounds/<stamp>-<label>/`: `round.json` (plan, variants, validity), `round-summary.json` (per-repeat
values), `stats.json`, `REPORT.md`, and per run the k6 summary, per-journey JSON, Prometheus window metrics
(`resources.json`), samples, events, profile check and `meta-{A,B}.json` (image tags + digests, git SHA/branch/dirty,
env, resources, Postgres image/args, seed counts before and after). Chaos runs: `thesis/results/chaos/`; Lighthouse:
`thesis/results/lighthouse-cluster-<date>/`; static: `thesis/results/static/<date>/`.

## 10. Decisions on the former open questions

| # | Question | Decision (2026-10-24) |
|---|---|---|
| OQ-1 | Comment deletion: tombstone (A) vs cascade (B) | Accepted as a documented difference; not on any measured path. |
| OQ-2 | Karma formula | Both = net reactions on the user's live content (A `user_stats_v` = Σ reaction value; B `AuthorStats` = up-votes + comment likes); accepted as equivalent. |
| OQ-3 | Anonymous WebSocket viewers in B | Kept (B's public broadcast model, deviation D-7). Since 2026-10-24 B delivers private-community comments to active members only (`broadcastComment` gate), so no private content reaches an outsider or anonymous socket. |
| OQ-4 | Field limits | Benchmark payloads stay far inside both systems' limits (titles < 100 chars, comment bodies ≈ 18 words, thread bodies 120–320 words, one 256×256 PNG ≈ 262 KB); A enforces title ≤ 200 / body ≤ 40 000 / comment ≤ 4 000 / upload ≤ 5 MiB, B bounds uploads at its media limit (images 5 MB) — the remaining differences are disclosed, not normalized. |
| OQ-5 | Shared VM budget with monitoring | One minikube profile (6 CPUs / 10 GiB) with A's monitoring stack only; B's ServiceMonitor is applied to it. |
| OQ-6 | HPA targets | Main profile: 2 fixed replicas, no HPA, both sides. Scaling class measured as the `--hpa` variant with the identical HPA (1–3 replicas @ 70 % CPU) on both. |
| OQ-7 | Social seed volumes | Both Benchmark seeds carry 2 400 accepted friendships (ring + chords 1/7/40), 80 pending requests (offset 13) and 2 000 direct messages over the ring pairs (A since 2026-10-24). |

## Sign-off

| Author | Architecture | Date | Signature/commit |
|---|---|---|---|
| Jakub Patkowski | A (forum-dotnet) | — | — |
| Hubert Ożarowski | B (gomx) | — | — |
