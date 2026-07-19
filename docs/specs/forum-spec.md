# Forum A/B Shared Functional Specification

> **STATUS: DRAFT — pending Hubert's review and sign-off.**
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

## 9. Measurement protocol

### 9.1 Profiles
smoke (sanity, 5 VU/60 s) · demo (ramp to 80 VU, HPA showcase) · stress (ramp to 150+ VU
until p95 journey > 500 ms — the H2 ceiling). Same ramps both sides; ≥3 repeats exploratory,
≥N (TBD with supervisor; target 10+) for hypothesis-tested tables; interleaved A,B,A,B; one
discarded warm-up each.

### 9.2 Units & thresholds
Journeys/sec (not raw RPS); p95/p99 per journey; error rate <1%; zero-429 guard; WS receipt
latency from write-ack to client receipt (measurement point per side documented).

### 9.3 Mix (proposal, from A's 9c profile)
J1 30% · J2 20% · J6 10% · J7 2% · J3 10% · J4 5% (of which 3% with upload) · J5 12% ·
J9 3% · remainder navigation; think 0.3–0.7 s; WS scenario parallel (20 demo / 40 stress VU).

### 9.4 Run artifacts
Per run: k6 summary JSON, meta.json (git SHA, image digest, seed counts, limiter values, VM
sizing, k6 version), infra samples (HPA/`kubectl top`), Prometheus query snapshots.
Layout: `thesis/results/{A,B}/<stamp>-<profile>/…` (A's existing layout; B mirrors — B-6).

### 9.5 Non-load metrics
- Lighthouse: 2 pages (feed, thread), 3 runs, same budgets file on both.
- Dev-loop: 10 runs each of clean build / incremental build / test wall-clock /
  edit→running-change / cold start, same machine, same day; B's `reload-bench` JSON schema.
- Trivy (pinned version) + SonarQube (one server) same-day on both repos.
- Dependency counts: direct from manifests; transitive from lockfile/module graph; rules
  documented in the results tables.
- Desktop: installer size + process-tree RSS/CPU during a scripted 5-minute session.

## 10. Open questions (blocking sign-off)

- **OQ-1** Comment deletion semantics: tombstone (A) vs cascade (B) — accept as documented
  difference, or align? (A-side view: accept; it doesn't touch measured journeys.)
- **OQ-2** Karma formula: pin exact definition (net reactions on live content) or accept
  near-equivalence.
- **OQ-3** Anonymous WS viewers in B: confirm and document.
- **OQ-4** B's field-length limits: verify actual enforced values.
- **OQ-5** Shared VM/minikube budget with both monitoring stacks.
- **OQ-6** HPA targets: unify (recommended: one common CPU target) or record-and-justify.
- **OQ-7** Social seed volumes (§7 friendships/DMs) — proposal needs B's ack; A must also
  extend its bench seed (currently 0 social rows — recorded as blocked-on-Hubert in
  `POST-9C-ROADMAP.md` Decision 3; this spec is the unblocking vehicle).

## Sign-off

| Author | Architecture | Date | Signature/commit |
|---|---|---|---|
| Jakub Patkowski | A (forum-dotnet) | — | — |
| Hubert Ożarowski | B (gomx) | — | — |
