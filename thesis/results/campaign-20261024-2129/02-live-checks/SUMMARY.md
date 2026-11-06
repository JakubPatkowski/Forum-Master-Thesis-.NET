# Phase 1 — live checks of both deployments (2026-10-24 21:32–21:37 CEST, before the gate)

Everything went through the shared ingress-nginx (`https://forum.local`, `https://gomx.local`, `https://minio.forum.local`;
TLS verified against the mkcert CA). Images under test: A `forum-dotnet-api:git-64cd5e2` / `forum-dotnet-web:git-64cd5e2`
(`thesis-v3-a`), B `gomx:bench-46b342c` (`thesis-v3-b`) — `../00-preflight/`. Single-shot timings are informational only
(one request, cold paths included); sizes are exact. Raw headers/bodies: `raw/`. Script: `live-checks.sh` (kept in the
operator scratchpad; its commands are reproduced by the table columns). The dataset was the fresh Benchmark seed of the
deploys; the checks themselves wrote 1–2 comments, 1 thread (+ 1 upload), 1 DM and 1 like/unlike per side (the gate and
every measured run reseed).

## Endpoints the h6 harness uses (status gzip/raw · bytes gzip · bytes raw · content-encoding · ms)

| target | journey | method | path | status | gzip B | raw B | enc | ms (gz) | note |
|---|---|---|---|---|---|---|---|---|---|
| A | nav | GET | `/api/content/categories` | 200/200 | 604 | 2 113 | gzip | 134.7 | anonymous — **8 public categories, 0 private** (v3 posture) |
| A | J1 feed p1 | GET | `/api/content/threads?categoryId=<bench-cat-06>&limit=20` | 200/200 | 3 775 | 14 264 | gzip | 111.7 | 20 cards, each with a **300-char `excerpt`** (v1: 1 565 / 8 004) |
| A | J1 batch | GET | `/api/engagement/reactions/batch?targetType=thread&targetIds=<20>` | 200/200 | 489 | 1 483 | gzip | 91.6 | like counts of the cards |
| A | J1 feed p2 | GET | `…&cursor=<keyset>` | 200/200 | 3 826 | 14 265 | gzip | 22.7 | keyset cursor |
| A | J2 thread | GET | `/api/content/threads/01KDY021004XKGMPA2XJZK3DPT` | 200/200 | 782 | 1 720 | gzip | 73.7 | heaviest public thread (same id as v1) |
| A | J2 comments | GET | `…/comments` | 200/200 | 23 375 | 101 084 | gzip | 64.5 | comment tree (235 comments incl. the live-check one) |
| A | J2 batch | GET | `/api/engagement/reactions/batch?targetType=comment&targetIds=<≤100>` | 200/200 | 1 954 | 7 401 | gzip | 30.6 | |
| A | J6 search (rank 1) | GET | `/api/content/search?q=abandon&limit=20` | 200/200 | 3 987 | 14 277 | gzip | 31.5 | 20 results, 0 private (v1 `kubernetes`: 1 804 / 8 016) |
| A | J6 search (rank 300) | GET | `/api/content/search?q=zephyr&limit=20` | 200/200 | 4 042 | 14 277 | gzip | 19.4 | 20 results, 0 private |
| A | J7 stats | GET | `/api/engagement/users/<bench_user_0034>/stats` | 200/200 | 144 | 144 | gzip | 15.9 | |
| A | J7 threads | GET | `/api/content/users/<bench_user_0034>/threads` | 200/200 | 456 | 753 | gzip | 13.1 | |
| A | J3 comment | POST | `/api/content/threads/<id>/comments` | 201 | 42 | – | – | 311.8 | authenticated (first write on a fresh pod) |
| A | J5 like / unlike | PUT / DELETE | `/api/engagement/reactions/thread/<id>` | 200 / 200 | 99 / 100 | – | gzip | 100.2 / 76.6 | the PUT was a no-op (user had liked it in the seed) → the DELETE removed a seeded like (A reactions 14 999 afterwards) |
| A | J4 initiate | POST | `/api/files` | 201 | 486 | – | – | 126.1 | presign, `uploadUrl` host `https://minio.forum.local` |
| A | J4 PUT | PUT | `https://minio.forum.local/forum/2026/10/<ulid>?<presign>` | 200 | 262 548 sent | – | – | 45.2 | 256×256 RGBA PNG straight to MinIO |
| A | J4 commit | POST | `/api/files/<id>/commit` | 200 | 128 | – | gzip | 239.4 | stat + magic bytes + IHDR |
| A | J4 thread | POST | `/api/content/threads` | 201 | 41 | – | – | 74.8 | untagged, `![photo](image:<id>)` in the body |
| A | J4 attach | POST | `/api/files/<id>/attachments` | 204 | 0 | – | – | 116.5 | target = the new thread |
| A | J9 friends | GET | `/api/social/friends?limit=50` | 200 | 453 | – | gzip | 28.5 | 6 friends (seeded ring) |
| A | J9 open | POST | `/api/social/conversations/direct` | 200 | 73 | – | gzip | 118.4 | get-or-create |
| A | J9 send | POST | `/api/social/conversations/<id>/messages` | 200 | 115 | – | gzip | 149.3 | |
| A | J8 ticket | POST | `/api/realtime/ticket` | 200 | 289 | – | gzip | 16.8 | → `wss …/api/realtime/ws?ticket=` **101** |
| A | spa home | GET | `/` | 200/200 | 3 740 | 16 589 | gzip | 177.3 | SPA shell |
| A | private thread / feed / comments / category slug | GET | | 404 ×4 | ~230 | ~270 | gzip | 10–35 | A-12 read gate + v3 category posture |
| A | `/health/ready`, `/metrics` | GET | | 404/404 | 3 123 | 13 678 | gzip | | not ingress-exposed (SPA 404 page) |
| B | nav | GET | `/communities` | 200/200 | 4 668 | 20 906 | gzip | 11.3 | anonymous |
| B | J1 space | GET | `/c/general` | 200/200 | 15 435 | 144 048 | gzip | 17.3 | 20 cards with **300-char previews** (v1: 16 205 / 174 116) |
| B | J1 feed p2 | GET | `/c/general/feed?page=2&cursor=…` | 200/200 | 10 614 | 126 660 | gzip | 13.4 | sentinel cursor |
| B | J2 thread | GET | `/thread/1` | 200/200 | 50 525 | 742 812 | gzip | 28.2 | heaviest public post, whole tree server-rendered (227 comments) |
| B | J6 search (rank 1) | GET | `/search?q=abandon` | 200/200 | 15 234 | 142 422 | gzip | 32.6 | (v1 `kubernetes`: 14 752 / 173 285) |
| B | J6 search (rank 300) | GET | `/search?q=zephyr` | 200/200 | 15 041 | 141 399 | gzip | 13.6 | |
| B | J7 profile | GET | `/u/34` | 200/200 | 5 855 | 25 619 | gzip | 11.0 | |
| B | J3 comment | POST | `/posts/1/comments` | 303 | 0 | – | – | 16.5 | → `/thread/1#comment-9001` |
| B | J5 vote ×2 | POST | `/posts/1/vote` (`HX-Request`) | 200 / 200 | 534 / 569 | – | gzip | 9.9 / 8.8 | server-side toggle, state restored |
| B | J4 upload | POST | `/uploads` (multipart) | 202 | 75 | – | – | 34.1 | status `completed` after 1 poll (0.05 s) |
| B | J4 post | POST | `/posts` | 303 | 0 | – | – | 10.8 | → `/thread/1601` (positive id, h6 rule) |
| B | J9 DM | POST | `/messages/28` | 303 | 0 | – | – | 10.3 | → `/messages/28` (no `?error=rejected`) |
| B | `/auth/google` | GET | | 303/303 | 57 | 57 | – | 7.8 | → `/login?error=oauth_unavailable`, only `gomx_locale` cookie |
| B | private space / post | GET | `/c/bench12`, `/thread/5` | 404/404 | 3 840 | 12 621 | gzip | 7.6 / 8.9 | |
| B | `/readyz`, `/healthz`, `/stats` | GET | | 200 | 2 / 2 / 67 | | – | 6–8 | reachable through the ingress (minor exposure difference vs A, as in v1) |
| B | `/metrics` | GET | | 404/404 | 3 840 | 12 621 | gzip | 6.0 | not exposed via ingress |

## v3 card sizes vs v1 (the D2/D11 question)

| page (gzip / raw bytes) | v1 A | v1 B | v1 B/A (gzip) | v3 A | v3 B | v3 B/A (gzip) |
|---|---|---|---|---|---|---|
| J1 space page 1 (A = feed + batch) | 2 054 / 9 487 | 16 205 / 174 116 | 7.9× | 4 264 / 15 747 | 15 435 / 144 048 | **3.6×** |
| J6 search page 1 | 1 804 / 8 016 | 14 752 / 173 285 | 8.2× | 3 987 / 14 277 | 15 234 / 142 422 | **3.8×** |

The **card text** is now equal (A: 20/20 excerpts exactly 300 chars; B: 300-char previews), so the content part of D2 is
closed. The **byte gap halved but did not disappear**: B's card *markup* (vote controls, HTMX attributes, author block,
icons) is ≈ 6.4 KB raw per card (128 KB of the 144 KB page) against A's ≈ 0.7 KB JSON per card. That remainder is the
HTML-vs-JSON paradigm (D-1), not a content asymmetry. (Correction to the expectation "the D2/D11 gap should be gone".)

## Posture / protocol findings (`live-checks.log`, `tls.txt`, `health.txt`, `limiter-and-runtime-env.txt`)

- **TLS**: both hosts TLSv1.3 / TLS_AES_256_GCM_SHA384, ALPN h2, one mkcert certificate (expires 2029-01-17).
  **Compression**: `content-encoding: gzip` on every compressible response on both sides (ingress; B's in-process gzip off).
- **A posture**: anonymous categories list 0 private / 8; private thread, private feed, private comments and the private
  category by slug → 404 (`application/problem+json`); anonymous search (`abandon`, `zephyr`) 20 results each, **0 private**
  (268 private threads in the DB). Login 200, JWT 380 chars, refresh cookie `secure; samesite=strict; httponly`,
  path `/api/identity`. WebSocket: ticket → **101**.
- **Finding (documented design, newly visible): A's ticket is single-use PER REPLICA.** One ticket, four handshakes →
  `101 101 401 401` (repeated: same) with 2 backend replicas — the redeemed-jti cache is in-process (CLAUDE.md Phase 7: "single-use
  via per-replica redeemed-jti cache (cross-replica replay bounded by the 30 s TTL)"). v1's single replay happened to hit the
  same replica (401). Not a regression; the bound is one socket per replica within 30 s.
- **B posture**: `/auth/google` → 303 `/login?error=oauth_unavailable`, no `forum_session`; anonymous private community and
  private post → 404; `/metrics` not exposed; login 303 + `forum_session` `HttpOnly; Secure; SameSite=Lax`. WebSocket `/ws`
  → **101 with the cookie and 101 anonymously** (B accepts unauthenticated sockets — D-7; the harness always connects with a
  session).
- **B private-comment WebSocket gate (thesis-v3-b) — live test PASS** (`b-ws-private-gate/`: k6 script, log, result.json):
  three sockets held 15 s (anonymous, non-member `benchuser0002`, member `benchuser0012` of private `bench12`); the member
  commented on private post 5 then public post 1. Private-comment frames received: anonymous **0**, non-member **0**,
  member **1**; the public comment reached all three (positive control).
- **Rate limiters / tracing / runtime (recorded)**: A `RateLimiting__{Global,Auth}__PermitLimit=1000000`,
  `Observability__TracingEnabled=false` (configmap base values 1000 / 50 are overridden; restored by the round's EXIT trap),
  `DOTNET_ThreadPool_MinThreads=64`; B `GOMX_SEED=bench`, `GOMX_AUTH_RATELIMIT=0`, `GOMX_BEACON_RATELIMIT=0`,
  `GOMX_COMPRESS=0`, `GOMX_NOTIFY_FRIENDS_ON_POST=0`, `GOMX_CARD_PREVIEW_CHARS=300`, `GOMX_DB_MAX_CONNS=30`,
  `OTEL_EXPORTER_OTLP_ENDPOINT=` (empty). Replicas 2 / 2, no HPA anywhere. Common-profile check 38/38 ok
  (`../00-preflight/ab_check_profile.txt`).
- **Health**: A `/health/live` + `/health/ready` 200 `Healthy`; B `/healthz` + `/readyz` 200 `ok` (port-forward).
  **Prometheus**: 19/19 targets UP incl. both gomx pods (`prometheus-targets.txt`).
- **Logs before the round** (`logs-before-round/`, `SUMMARY.txt`): A backend — the known startup warnings (ephemeral
  DataProtection keys, HTTP_PORTS override) and the Kerberos-probe line of the distroless image; **plus an environmental
  incident at 21:25 CEST**: while B's image was being built on the same node (Go compile + bun install), A's RabbitMQ failed
  its exec liveness probe (`rabbitmq-diagnostics`, 10 s timeout) and was restarted (restartCount 11 → 12), A's Postgres
  probes timed out once, and backend pod `fl727` logged broker loss + `/health/ready` 503 for about a minute (reconnected
  21:26:28). Cause: node CPU starvation during a build, before any measurement; the round builds nothing and restarts A's
  app pods before every run. B — only the two OAuth-refusal WARNs caused by the posture probe; B Postgres
  `relation "goose_db_version" does not exist` once (fresh DB, benign).

## Seed shape after the deploys (`seed-shape-A.txt`, `seed-shape-B.txt`; includes the live-check writes)

| property | A | B |
|---|---|---|
| users / threads / comments / reactions / DMs (`ab_seed_counts`, before the checks) | 800 / 1 600 / 9 000 / 15 000 / 2 000 | 800 / 1 600 / 9 000 / 15 000 / 2 000 |
| threads in the 3 hot spaces | 316 (incl. the J4 thread), 341, 332 | 330, 340 (incl. the J4 post), 332 |
| private threads | 268 (266 live) | 263 |
| public live threads (+1 J4 each) | 1 318 | 1 338 |
| comments per public thread mean / median / max | 5.72 / 4 / 235 | 5.67 / 4 / 227 |
| avg body chars (Zipf bank, v2+) | 1 718 | 1 727 |
| reactions thread / comment | 11 219 / 3 780 (one seeded like removed by the J5 check) | 11 250 / 3 750 |
| friendships accepted / pending, DMs | 2 400 / 80, 2 001 | 2 400 / 80, 2 001 |
| DB size | 34 MB | 35 MB |

Note: A's reaction split appears to be 11 220 / 3 780 in the seed itself (B 11 250 / 3 750) — a 0.3 % difference in which
targets carry the likes; totals are equal. Re-checked on the pristine reseed in Phase 4 (`sql-per-journey/`).
