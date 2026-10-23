# Phase 2 — live checks of both deployments (2026-10-18 00:13–00:18 CEST, after a pristine reseed)

All requests went through the shared ingress-nginx (`https://forum.local` / `https://gomx.local`, TLS verified against the mkcert CA, HTTP/2). Single-shot timings are informational only (host not isolated at that moment); sizes are exact. Raw headers/bodies: `raw/`. Versions under test: `../00-preflight/`.

## Endpoints the harness uses (status gzip/raw · bytes gzip · bytes raw · content-encoding · ms gzip · ms raw)

| target | journey | method | path | status | gzip B | raw B | enc | ms (gz) | ms (raw) | note |
|---|---|---|---|---|---|---|---|---|---|---|
| A | nav | GET | `/api/content/categories` | 200/200 | 824 | 3177 | gzip | 84.8 | 129.2 | anonymous |
| A | J1-feed-p1 | GET | `/api/content/threads?categoryId=01KE8D2RM0R1FEQEKPENWN6VHX&limit=20` | 200/200 | 1565 | 8004 | gzip | 67.9 | 14.1 | anonymous, public category |
| A | J1-batch | GET | `/api/engagement/reactions/batch?targetType=thread&targetIds=01KDY02…` | 200/200 | 489 | 1483 | gzip | 79.7 | 9.9 | 20 feed ids |
| A | J1-feed-p2 | GET | `/api/content/threads?categoryId=01KE8D2RM0R1FEQEKPENWN6VHX&limit=20…` | 200/200 | 1527 | 8005 | gzip | 16.5 | 82.3 | keyset cursor |
| A | J2-thread | GET | `/api/content/threads/01KDY021004XKGMPA2XJZK3DPT` | 200/200 | 772 | 1837 | gzip | 69.2 | 24.8 | public thread detail |
| A | J2-comments | GET | `/api/content/threads/01KDY021004XKGMPA2XJZK3DPT/comments` | 200/200 | 22829 | 103509 | gzip | 72.9 | 63.9 | comment tree |
| A | J2-batch | GET | `/api/engagement/reactions/batch?targetType=comment&targetIds=01KE0P…` | 200/200 | 1954 | 7401 | gzip | 11.1 | 29.7 | comment ids |
| A | J6-search | GET | `/api/content/search?q=kubernetes&limit=20` | 200/200 | 1804 | 8016 | gzip | 33.5 | 20.1 | anonymous FTS |
| A | J7-stats | GET | `/api/engagement/users/01KDYYYSM0PH4QS1MMRNJ8796B/stats` | 200/200 | 144 | 144 | gzip | 16.3 | 11.0 | profile stats |
| A | J7-threads | GET | `/api/content/users/01KDYYYSM0PH4QS1MMRNJ8796B/threads` | 200/200 | 322 | 440 | gzip | 12.3 | 12.9 | profile threads |
| A | J3-comment | POST | `/api/content/threads/01KDY021004XKGMPA2XJZK3DPT/comments` | 201/201 | 42 | 42 | – | 199.1 | 18.2 | authenticated write (2 comments created) |
| A | J5-like | PUT | `/api/engagement/reactions/thread/01KDY021004XKGMPA2XJZK3DPT` | 200/200 | 99 | 74 | gzip | 65.6 | 15.6 | authenticated (idempotent) |
| A | J5-unlike | DELETE | `/api/engagement/reactions/thread/01KDY021004XKGMPA2XJZK3DPT` | 200/200 | 100 | 75 | gzip | 262.6 | 18.7 | authenticated (idempotent) |
| A | J8-ticket | POST | `/api/realtime/ticket` | 200/200 | 289 | 324 | gzip | 18.6 | 9.3 | single-use ticket |
| A | sec-private-thread | GET | `/api/content/threads/01KDY56TE07KFE00T6ZSDGW9GS` | 404/404 | 228 | 270 | gzip | 25.2 | 22.6 | A-12: anonymous → expect 404 |
| A | sec-private-feed | GET | `/api/content/threads?categoryId=01KEQVF2M0AAE852YKBPSGN18K&limit=20` | 404/404 | 229 | 274 | gzip | 9.2 | 11.6 | A-12: anonymous → expect 404 |
| A | sec-private-comments | GET | `/api/content/threads/01KDY56TE07KFE00T6ZSDGW9GS/comments` | 404/404 | 228 | 270 | gzip | 9.3 | 11.3 | A-12: anonymous → expect 404 |
| A | sec-health-ready | GET | `/health/ready` | 404/404 | 3128 | 13678 | gzip | 11.2 | 24.4 | not ingress-exposed by design |
| A | sec-metrics | GET | `/metrics` | 404/404 | 3128 | 13678 | gzip | 8.7 | 8.4 | not ingress-exposed by design |
| A | spa-home | GET | `/` | 200/200 | 3747 | 16589 | gzip | 9.0 | 8.0 | SPA shell HTML |
| B | nav | GET | `/communities` | 200/200 | 4666 | 20906 | gzip | 11.7 | 10.4 | anonymous |
| B | J1-space | GET | `/c/general` | 200/200 | 16205 | 174116 | gzip | 19.1 | 19.5 | anonymous, public space (20 cards + body previews) |
| B | J1-feed-p2 | GET | `/c/general/feed?page=2&cursor=eyJwIjpmYWxzZSwiYyI6IjIwMjYtMTAtMTdUM…` | 200/200 | 11337 | 160668 | gzip | 17.1 | 16.4 | sentinel cursor page |
| B | J2-thread | GET | `/thread/1` | 200/200 | 45038 | 745261 | gzip | 40.1 | 30.6 | public thread page (tree server-rendered) |
| B | J6-search | GET | `/search?q=kubernetes` | 200/200 | 14752 | 173285 | gzip | 40.1 | 41.1 | anonymous search |
| B | J7-profile | GET | `/u/34` | 200/200 | 5622 | 20746 | gzip | 10.0 | 10.9 | profile page |
| B | J3-comment | POST | `/posts/1/comments` | 303/303 | 0 | 0 | – | 12.6 | 8.9 | authenticated write → 303 (2 comments created) |
| B | J5-vote | POST | `/posts/1/vote` | 200/200 | 534 | 1489 | gzip | 11.8 | 10.1 | authenticated toggle ×2 (state restored) |
| B | sec-oauth | GET | `/auth/google` | 303/303 | 57 | 57 | – | 5.8 | 5.8 | B-03: expect 303 → /login?error=oauth_unavailable, no session cookie |
| B | sec-private-space | GET | `/c/bench12` | 404/404 | 3841 | 12621 | gzip | 6.5 | 6.6 | anonymous private space |
| B | sec-private-thread | GET | `/thread/5` | 404/404 | 3841 | 12621 | gzip | 7.1 | 6.8 | anonymous private post |
| B | readyz | GET | `/readyz` | 200/200 | 2 | 2 | – | 6.5 | 5.9 | via ingress |
| B | healthz | GET | `/healthz` | 200/200 | 2 | 2 | – | 5.8 | 6.4 | via ingress |
| B | stats | GET | `/stats` | 200/200 | 73 | 73 | – | 7.8 | 8.8 | version/counters endpoint |
| B | metrics | GET | `/metrics` | 404/404 | 3841 | 12621 | gzip | 6.7 | 5.9 | expect not exposed via ingress |

## Posture / protocol findings (`live-checks.log`, `tls.txt`, `health.txt`, `limiter-and-runtime-env.txt`)

- **TLS**: both hosts TLSv1.3 / TLS_AES_256_GCM_SHA384, ALPN h2, same mkcert certificate (expires 2029-01-17). **Compression**: `content-encoding: gzip` on every compressible response on both sides (from the ingress; B's in-process gzip is off per the bench overlay). A JSON feed page 8 004 → 1 565 B; B HTML space page 174 116 → 16 205 B (D2/D11); B thread page 745 261 → 45 038 B for the 227-comment thread vs A's tree 103 509 → 22 829 B (236 comments incl. the 2 live-check comments; D1: A's adapter also fetches detail + batch).
- **A security posture (A-12)**: anonymous private thread detail / private category feed / private thread comments → **404** (`application/problem+json`); anonymous search for `kubernetes`: 20 results, **0** from private categories (268 private threads exist). `/health/*` and `/metrics` are **not** ingress-exposed (404 from the SPA shell) — probes answer 200 `Healthy` on the Service port (port-forward). Login 200 with a bearer JWT (380 chars) + `refresh_token` cookie. WebSocket: ticket → `HTTP/1.1 101`; replaying the same ticket → **401** (single-use).
- **B security posture (B-03)**: `/auth/google` → **303** `/login?error=oauth_unavailable`, no `forum_session` cookie (only `gomx_locale`); anonymous private community `/c/bench12` and private post `/thread/5` → **404**; `/metrics` → 404 via ingress; `/healthz` + `/readyz` 200 `ok` (also reachable through the ingress — a minor exposure difference vs A, not measured). Login 303 + `forum_session` cookie `HttpOnly; Secure; SameSite=Lax`. J3 → 303 `Location: /thread/1#comment-9001`. WebSocket `/ws` → **101 with the cookie and 101 anonymously** (B accepts unauthenticated sockets and broadcasts to every socket — D-7; the harness always connects with a session).
- **Rate limiters / tracing (recorded, both neutralized)**: A `RateLimiting__Global__PermitLimit=1000000`, `RateLimiting__Auth__PermitLimit=1000000`, `Observability__TracingEnabled=false`; B `GOMX_AUTH_RATELIMIT=0`, `GOMX_BEACON_RATELIMIT=0`, `GOMX_COMPRESS=0`, `GOMX_NOTIFY_FRIENDS_ON_POST=0`, `GOMX_DB_MAX_CONNS=30`, `OTEL_EXPORTER_OTLP_ENDPOINT=` (empty). Common-profile check: 34/34 ok (`../00-preflight/ab_check_profile.txt`).
- **Health**: A `/health/live` + `/health/ready` 200 `Healthy`; B `/healthz` + `/readyz` 200 `ok`. **Prometheus**: 19/19 targets UP (`prometheus-targets.txt`). **Logs before the round** (`logs-before-round/`): A backend — only the 5 known startup warnings (ephemeral DataProtection key ring, HTTP_PORTS override); B — no warnings/errors (`error tracking disabled (GLITCHTIP_DSN unset)` is INFO).
- **Seed shape after the reseed** (`seed-shape-A.txt`, `seed-shape-B.txt`): both 800 users / 1600 threads / 9000 comments / 15000 reactions; see the files for per-space counts, comments-per-public-thread distribution, J6 hit counts and DB sizes.
- **A SPA functional regression (not on the harness path)**: anonymous `/` shows "SIGNAL LOST — Category not found" (`screenshots/A-home.png`): the home feed merges all 12 categories returned by `GET /api/content/categories` and the A-12 read gate answers 404 for the 4 private ones, which rejects the whole merge. `/c/{slug}`, `/t/{id}`, `/search`, `/u/{id}` render correctly (`screenshots/A-*.png`). Root cause + fix candidate recorded in `../PROGRESS.md`.

## SQL statements per journey on the live cluster databases (`sql-per-journey/`)

Method: `log_statement=all` for ~85 s on both Postgres pods (ALTER SYSTEM + reload, reset and verified `none` afterwards), psql markers around one request per journey (after two warm-up passes), background statements classified by pattern. Only the first line of a multi-line statement is logged in the classified line; the A J1/J2 windows contain the feed/comment-tree request twice (the script re-fetched the body to build the batch ids), so divide those by two. Reference: `thesis/results/AB-COMPARISON-v1.md` §5.6.

| window | A foreground | A background (relay polls / Npgsql resets / tx / health / exporter) | B foreground | B background (River polls / pgx pings / leader / tx) |
|---|---|---|---|---|
| nav (A categories | B /communities) | 1 | 21 {'Npgsql pooled-connection reset (D9)': 5, 'tx control (BEGIN/COMMIT/ROLLBACK)': 10, 'outbox relay poll': 5, 'postgres-exporter (monitoring)': 1} | 1 | 10 {'tx control (begin/commit/rollback)': 2, 'River job queue poll': 5, 'pgx pool ping': 3} |
| J1 page 1 (A feed | B /c/general) | 4 | 26 {'Npgsql pooled-connection reset (D9)': 10, 'health check SELECT 1': 1, 'tx control (BEGIN/COMMIT/ROLLBACK)': 10, 'outbox relay poll': 5} | 0 | 10 {'River job queue poll': 6, 'pgx pool ping': 2, 'tx control (begin/commit/rollback)': 2} |
| J1 reaction batch (A only) | 2 | 21 {'Npgsql pooled-connection reset (D9)': 6, 'tx control (BEGIN/COMMIT/ROLLBACK)': 10, 'outbox relay poll': 5} | 2 | 10 {'pgx pool ping': 5, 'River job queue poll': 4, 'River leader election': 1} |
| J2 detail + comment tree (A) | /thread/1 (B) | 5 | 7 {'Npgsql pooled-connection reset (D9)': 4, 'health check SELECT 1': 1, 'postgres-exporter (monitoring)': 2} | 0 | 11 {'pgx pool ping': 6, 'River job queue poll': 4, 'River leader election': 1} |
| J2 reaction batch (A only) | 2 | 23 {'Npgsql pooled-connection reset (D9)': 5, 'tx control (BEGIN/COMMIT/ROLLBACK)': 10, 'outbox relay poll': 5, 'postgres-exporter (monitoring)': 2, 'health check SELECT 1': 1} | 3 | 7 {'pgx pool ping': 3, 'River job queue poll': 4} |
| J6 search | 1 | 21 {'Npgsql pooled-connection reset (D9)': 6, 'tx control (BEGIN/COMMIT/ROLLBACK)': 10, 'outbox relay poll': 5} | 1 | 10 {'River job queue poll': 5, 'pgx pool ping': 4, 'River leader election': 1} |
| J7 profile (A stats + threads | B /u/34) | 2 | 24 {'Npgsql pooled-connection reset (D9)': 8, 'health check SELECT 1': 1, 'tx control (BEGIN/COMMIT/ROLLBACK)': 10, 'outbox relay poll': 5} | 3 | 10 {'pgx pool ping': 5, 'River job queue poll': 4, 'River leader election': 1} |
| J3 comment create (+6 s for relay/consumers) | 5 | 69 {'Npgsql pooled-connection reset (D9)': 15, 'tx control (BEGIN/COMMIT/ROLLBACK)': 32, 'outbox relay poll': 17, 'postgres-exporter (monitoring)': 4, 'health check SELECT 1': 1} | 4 | 43 {'tx control (begin/commit/rollback)': 6, 'River leader election': 6, 'River job queue poll': 17, 'pgx pool ping': 14} |
| J5 reaction/vote (+6 s for relay/consumers) | 3 | 67 {'Npgsql pooled-connection reset (D9)': 20, 'tx control (BEGIN/COMMIT/ROLLBACK)': 30, 'outbox relay poll': 15, 'psql marker/meta': 1, 'health check SELECT 1': 1} | 6 | 45 {'pgx pool ping': 15, 'River job queue poll': 16, 'tx control (begin/commit/rollback)': 8, 'River leader election': 6} |
| idle 20 s | 0 | 188 {'Npgsql pooled-connection reset (D9)': 49, 'tx control (BEGIN/COMMIT/ROLLBACK)': 90, 'outbox relay poll': 45, 'health check SELECT 1': 4} | 0 | 116 {'pgx pool ping': 39, 'River job queue poll': 48, 'River leader election': 13, 'tx control (begin/commit/rollback)': 16} |

Reading: foreground counts reproduce §5.6 on the live cluster — A nav 1 (B nav **1**: the 2026-10-18 anonymous-membership fix is live), A J1 = read-gate + feed (+ 2-statement batch), A J2 = detail + (read-gate + tree) + 2-statement batch = 5, A J6 1, A J7 2; B J1 2, J2 3, J6 1, J7 3; A J3 = thread + `has_permission` + INSERT (comment + outbox in one batch with an EF savepoint); A J5 = target⋈category + `has_permission` + existing-reaction read (+ the write batch); B J3 = session + post + community + INSERT; B J5 = session + `FOR UPDATE` + vote dir + delete/upsert + `SumPostVotes` + vote dir.
**Idle 20 s, 2 replicas each, no traffic**: A **188** background statements (45 outbox-relay polls = 5 relays × 2 replicas / 5 s, each wrapped in BEGIN/ROLLBACK and followed by the 7-statement Npgsql reset; 4 health `SELECT 1`) ≈ 9.4 statements/s; B **116** (48 River `JobGetAvailable`, 39 pgx `-- ping`, 13 leader-election statements, 16 begin/commit) ≈ 5.8 statements/s. This is the standing DB chatter behind the "idle cores" rows of the round report.

## Screenshots (`screenshots/`, 1366×2200, chrome-headless-shell, Benchmark data)

- `A-home.png` — https://forum.local/ (87400 bytes)
- `A-feed.png` — https://forum.local/c/bench-cat-06 (170917 bytes)
- `A-thread.png` — https://forum.local/t/01KDY021004XKGMPA2XJZK3DPT (183173 bytes)
- `A-search.png` — https://forum.local/search?q=kubernetes (160798 bytes)
- `A-profile.png` — https://forum.local/u/01KDYYYSM0PH4QS1MMRNJ8796B (97085 bytes)
- `B-home.png` — https://gomx.local/ (384045 bytes)
- `B-feed.png` — https://gomx.local/c/general (367832 bytes)
- `B-thread.png` — https://gomx.local/thread/1 (174900 bytes)
- `B-search.png` — https://gomx.local/search?q=kubernetes (233743 bytes)
- `B-profile.png` — https://gomx.local/u/34 (121882 bytes)
