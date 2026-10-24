# Benchmark guardrails for Architecture B (`gomx`)

> **Audience:** Hubert and every AI coding agent working in the `gomx` repository (Copilot, Claude, Codex, …).
> **Status:** binding from 2026-10-24 until the thesis measurements are closed (v2 round + variants + chaos +
> Lighthouse + static metrics). Written by the A side; companion to `forum-spec.md` (what both systems must do)
> and `thesis/results/AB-COMPARISON-v1.md` (how it is measured). Questions → ask Jakub before acting, not after.
>
> **How to use this file in gomx:** copy it to `docs/BENCHMARK-GUARDRAILS.md` and add one line to
> `.github/copilot-instructions.md` (and `AGENTS.md` if present):
> `Before changing anything on feature/master-thesis or anything listed in docs/BENCHMARK-GUARDRAILS.md, read that file and follow it.`

---

## 0. TL;DR for an AI agent

1. The **measured** version of B is the git tag **`thesis-v3-b`** (branch `feature/master-thesis`; the commit the
   owner tags after the 2026-10-24 pre-measurement audit — `thesis-v2-b` = `a457114` is the state before it).
   It is frozen. Do not move the tag, do not force-push the branch, do not rebase it.
2. **`master` is free to evolve** — product work (passkeys, PWA, iOS, Templbook, refactors) belongs there.
   **Nothing from `master` is merged, cherry-picked or rebased into `feature/master-thesis`** without a written
   agreement with Jakub. `master` is NOT what the thesis measures.
3. On `feature/master-thesis`, only **bug fixes agreed with Jakub** are allowed, and each one must keep every
   invariant in §3 and pass every gate in §6. A fix = new commit + new tag (`thesis-v2.1-b`), never a moved tag.
4. If you are unsure whether a change touches the benchmark surface (§2), assume it does and stop.

## 1. Why this exists

A and B were aligned over several sessions so that the numbers compare *architectures*, not accidents
(fairness review 2026-10-17, re-verification 2026-10-18, comparability fixes 2026-10-24). Every alignment item
is cheap to break and expensive to re-establish: one new per-IP limiter turns a 2-hour round into hundreds of
429s; one "small perf fix" on one side makes the comparison unfair; one changed HTML attribute silently breaks
the k6 adapter. Measured rounds take 2–6 h each — a broken round costs a day.

## 2. The benchmark surface (what the measurements touch)

Changing any of these on `feature/master-thesis` changes the experiment.

### 2.1 HTTP routes driven by the k6 adapter (`forum-dotnet/load/k6/targets/b.js`)

| Journey | Request | Contract the adapter relies on |
|---|---|---|
| login (setup) | `POST /login` form `username`,`password` | 303 + `Set-Cookie: forum_session`; 422 on failure |
| pools (setup) | `GET /communities` | links `href="/c/<slug>"` |
| friends (setup) | `GET /messages` | links `/messages/<userId>` |
| J1 space feed | `GET /c/{slug}` | 20 × `<article id="post-N">` each with an author link `href="/u/N"`; next page = `hx-get` of `id="feed-sentinel"` |
| J2 thread open | `GET /thread/{id}` | full page, all comments rendered (see §3.3) |
| J3 comment | `POST /posts/{id}/comments` form `body` | 303, `Location: /thread/{id}#comment-{new}` |
| J4 thread + photo | `POST /uploads` multipart `file` → 202 `{upload_id}`; `GET /uploads/{id}/status` polls; `POST /posts` form `community`,`title`,`body` with `![](/uploads/<id>)` | 303, `Location: /thread/{id}` with `id > 0` (rejected → 303 `/`; a failed INSERT is a rejection since thesis-v3-b, never `/thread/0`) |
| J5 vote toggle | `POST /posts/{id}/vote` form `dir=up` + `HX-Request: true` | 200 fragment, server-side toggle; 404 when the post is missing OR in a community the voter may not see (thesis-v3-b — the gate A applies too) |
| J6 search | `GET /search?q=<word>` | 200 HTML |
| J7 profile | `GET /u/{id}` | 200 HTML |
| J9 DM | `POST /messages/{peerId}` form `body` | 303 `/messages/{peerId}` when accepted; 303 `/messages/{peerId}?error=rejected` when rejected (thesis-v3-b; the adapter counts the marker as a failed write) |
| J8 WebSocket | `GET /ws` (cookie) | JSON messages with `html` containing `id="comment-N"` for new comments |

Renaming a route, a form field, a cookie, the status code, the redirect target or any of the quoted HTML
markers breaks the adapter. If such a change is unavoidable, tell Jakub first — the adapter and the
harness version (`HARNESS_VERSION` in `load/k6/main.js`) must change in the same step.

### 2.2 Files that implement the alignment (do not edit casually)

| File | What it guarantees |
|---|---|
| `deploy/k8s/bench/*` (overlay + `README.md`) | the benchmark deployment shape (§3.1) |
| `app/forum/shared/seedbench.go` (+ test) | the deterministic Benchmark dataset (§3.2) |
| `app/forum/shared/postgres/postgres.go` (`applyPoolEnv`) | pool size from `GOMX_DB_MAX_CONNS` (A-shaped pool) |
| `app/forum/shared/rules.go`, `feed/service.go`, `community/service.go` (`benchCommunityIndex`) | A-shaped post placement, private/public split |
| `app/forum/web/cardpreview.go` | `GOMX_CARD_PREVIEW_CHARS` card excerpt = same card content as A |
| `app/forum/web/live.go` (`broadcastComment`, `broadcastPost`) | private-community content never reaches outsiders or anonymous sockets |
| `app/forum/hub/hub.go` (`subscriberQueueSize = 256`) | slow-consumer tolerance under load |
| `app/forum/web/http.go` (499 on client cancel) | cancelled requests are not counted as server errors |
| `app/forum/web/handlers_community.go` | anonymous browse does no membership lookups (N+1 fix) |
| `app/forum/feed/service.go` (`Vote`, `LikeComment`, `CreatePost`) | reactions on private-community content are rejected for non-members (the gate A's J5 pays too); a failed post INSERT is reported as a rejection |
| `app/forum/web/handlers_messages.go` (`handleSendDM`) | a rejected DM redirects with `?error=rejected` — the only way the harness can tell it from a delivered one |
| `app/forum/web/handlers_search.go` | search semantics matched to A (§3.4) |
| `internal/logging/logging.go`, `internal/server/router.go`, `cmd/server/main.go` | env wiring of the flags above |

## 3. Invariants that must hold on the measured build

### 3.1 Deployment shape (checked automatically by `ab_check_profile` in `forum-dotnet/scripts/lib-bench.sh`)
- 2 fixed app replicas, **no HPA** in the main profile; requests 250m/256Mi, limits 1 CPU / 512 MiB; probe timeouts 5 s.
- PostgreSQL `postgres:18` (Debian, 18.4), `shared_buffers=256MB effective_cache_size=512MB max_connections=100`,
  250m/512Mi → 1 CPU / 1 GiB; app pool 30 per replica.
- Bench overlay env (exact): `GOMX_SEED=bench`, `GOMX_AUTH_RATELIMIT=0`, `GOMX_BEACON_RATELIMIT=0`,
  `GOMX_COMPRESS=0` (gzip is done by the shared ingress for both sides), `GOMX_NOTIFY_FRIENDS_ON_POST=0`,
  `GOMX_CARD_PREVIEW_CHARS=300`, `GOMX_DB_MAX_CONNS=30`, `GOMX_SECURE_COOKIES=1`, `GOMX_ALLOWED_ORIGINS=gomx.local`,
  `GOMX_ENV=benchmark`, OAuth mock off, tracing off.
- **Any rate limiter that can trigger under k6 must be off in the overlay.** All k6 traffic arrives from one
  client IP; a single 429 invalidates the run. A new limiter = a new `…=0` line in the overlay **and** a new
  expectation in `ab_check_profile`, in the same change.

### 3.2 Dataset (`GOMX_SEED=bench`) — identical volumes to A
800 users (200 ordinary bench users with the known password used by k6) · 12 spaces (8 public + 4 private,
25 members each, A-shaped placement) · 1 600 threads (1 % pinned) · 9 000 comments (depth 0–4) · 15 000
reactions (75/25 thread/comment, Zipf) · 2 400 accepted + 80 pending friendships (ring + chords 1/7/40, offset 13)
· 2 000 direct messages on the ring pairs · the shared **300-word Zipf(1.2) search bank**, byte-identical to A's
`SeedText.cs` and k6 `assets.js` (splitmix64 ranks; no word is a substring of another).
Do not change counts, the word bank, the random streams or the placement rule. Run metadata records seed counts
before and after every run; a mismatch flags the run.

### 3.3 Per-request work must stay comparable to A
- Feed/search/profile card = title + metadata + 300-char excerpt (A does the same). Thread page = full body.
- Thread page renders **all** comments of the thread (A's thread view loads the whole tree too).
  Comment paging (e.g. master's `commentPageSize = 20`, migration `00022_comment_pages.sql`) changes J2's work
  and is **not** allowed on the measured build without the same change on A.
- No response caching that k6 can hit (ETag/304, server-side page caches) — k6 sends no `If-None-Match`, so ETags
  are neutral today, but a server-side cache would not be. Redis stays B's pub/sub + sessions, as measured.
- No one-sided performance work. If you find a real performance bug on B, report it; Jakub decides whether the
  equivalent check is done on A and both go in together. (Precedent: B's anonymous-membership N+1 and A's missing
  owner indexes were fixed in the same session.)

### 3.4 Behaviour (from `forum-spec.md` §3)
- Private spaces: content hidden from non-members on every path, including WebSocket pushes and search.
- Reaction toggle idempotent per user; comment depth ≤ 5; failed login non-revealing.
- Search: case-insensitive token match over title + body; with the shared bank `ILIKE` ≡ A's token match.
- WebSocket: anonymous viewers allowed (documented deviation D-7) but never receive private content.

## 4. What is allowed

| Where | Allowed |
|---|---|
| `master` and feature branches off it | anything — product features, refactors, new limiters, PWA, passkeys, iOS. It is not measured. |
| `feature/master-thesis` | only agreed bug fixes; new tag `thesis-v2.<n>-b`; Jakub reruns the affected rounds |
| docs on either branch | free, as long as they do not contradict this file |

## 5. If you want `master` measured ("B-master" variant)

This is possible after the v2 round, as a separate, labelled variant — never as a silent replacement.
Before asking for it, the master build must satisfy every item of §3, which today means at least:
1. bench overlay ported to master's deployment and extended with `GOMX_CONTENT_RATELIMIT=0` and
   `GOMX_UPLOAD_RATELIMIT=0` (master's defaults 60/min and 30/min would 429 the run), plus any newer limiter;
2. comment paging disabled for the bench (or the same paging added on A — a joint decision);
3. `GOMX_TRUSTED_PROXIES` reviewed against the shared ingress (the limiters key on the client IP);
4. the 12 merge conflicts with `feature/master-thesis` resolved without losing any file in §2.2
   (test: every test listed in §6 still exists and passes);
5. all §2.1 HTML markers still present (run `make ab-round ARGS="--plan smoke:1 --label bmaster-dry"` on A's side);
6. a new tag, and a short changelog of everything that changed on the measured surface.

## 6. Gates before any commit to `feature/master-thesis`

```bash
gofmt -l . && templ fmt -fail . && go vet ./...
go test -race ./...                          # incl. seedbench_test, live_private_test, cardpreview_test,
                                             #      handlers_community_test (N+1), postgres_test (pool env)
golangci-lint run
templ generate && git diff --exit-code       # generated code up to date
```
Plus the Postgres lane from CI. Then tell Jakub the new commit/tag; he redeploys with
`make gomx-deploy ARGS=--reset-db` and runs `ab_check_profile` + a smoke round before any measured round.

## 7. Never

- Move or delete `thesis-v2-b` (or any `thesis-*` tag); force-push or rebase `feature/master-thesis`.
- Merge `master` into `feature/master-thesis` "to keep it up to date".
- Add a limiter, cache, compression, prefetch or paging to the measured build without §3/§5.
- Change the seed, the word bank or the overlay "temporarily" for a local experiment and commit it.
- Edit anything in the `forum-dotnet` repository (harness, scripts, A code) — send a request instead.

## 8. Contact and sign-off

Changes to this file are agreed by both authors. Measured versions: A `thesis-v3-a`, B `thesis-v3-b` (the commits
tagged after the 2026-10-24 audit; `thesis-v2-a` = `e838b3f` / `thesis-v2-b` = `a457114` were the pre-audit state).

| Author | Agreed (date) |
|---|---|
| Jakub Patkowski (A) | 2026-10-24 |
| Hubert Ożarowski (B) | — |
