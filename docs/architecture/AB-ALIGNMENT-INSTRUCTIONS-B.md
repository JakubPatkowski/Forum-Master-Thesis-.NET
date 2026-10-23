# A/B Alignment — Implementation Instructions for Architecture B (gomx)

**Audience:** Hubert Ożarowski (owner of `gomx`). This document is self-contained: it explains the
thesis fairness rules, every change we ask for, why each is needed, exact files and code, how to
self-check without access to Jakub's cluster, and what is deliberately **not** asked of you.

| | |
|---|---|
| **Purpose** | Make `gomx` (Architecture B) and `forum-dotnet` (Architecture A) honestly comparable at the lowest cost, in time for the first class with real preliminary numbers (2026-10-17), and set up the remaining four classes. |
| **Snapshot — B** | `gomx` branch `master` @ `8b23bc1` ("Merge branch 'feat/mobile-optimizations'", 2026-10-15). Every `path:line` below was verified against this commit on 2026-10-16. |
| **Snapshot — A** | `forum-dotnet` branch `29-feat-phase-12---tauri-desktop-application` @ `ee0faee` + staged (uncommitted) work. |
| **Companion** | `AB-ALIGNMENT-INSTRUCTIONS-A.md` (Jakub's side — same decision table, same interface contract, plus the Day-0 runbook). The shared contract draft is `docs/specs/forum-spec.md` (still DRAFT, your sign-off pending). |
| **How to use** | Work top-down through §3 (P0 first). Each item lists the file, the before/after, the test to touch, and the exact command that proves it done. §5 is the minimum for tomorrow. Create a branch `ab-benchmark` from `master`; Jakub builds the image from that branch. |
| **Priority legend** | **P0** = needed for tomorrow's measurements · **P1** = next classes (within ~2 weeks) · **P2** = optional / hygiene. |

> **Status 2026-10-18 (night) — independent re-verification.** One more B edit on top of `f03d46a`, for review:
> `app/forum/web/handlers_community.go` no longer calls `Membership()` for an anonymous viewer (`uid == 0`) on
> `/communities` (was one `GetMembership` query per community — 12 per page) and on `/c/{slug}` (one per view);
> private spaces stay hidden / 404 exactly as before. Test `TestAnonymousBrowseSkipsMembershipLookups` +
> `newTestServerWithStore` in `web_test.go`. Replayed in the CI image: lint:go, `go test -race ./...`, Postgres
> lane — green. Full findings: forum-dotnet `docs/architecture/AB-START-HERE.md` §4.2/§5.0.
>
> **Status 2026-10-17 (evening) — review of `feature/master-thesis` @ `e13f18f`.** B-01…B-04, B-07…B-10 and
> B-12…B-14 verified (tests, vet, gofmt, golangci-lint 1.64.5 green; B-08 seen live: two pods, one seed). Edits made
> on top of the branch during the review (uncommitted, to be pushed to the same branch — please review):
> (1) **B-05a was missing** — the overlay put `pool_max_conns=30` into `DATABASE_URL`, which crashes startup exactly
> as described in B-05; now `postgres.go applyPoolEnv` reads `GOMX_DB_MAX_CONNS` (+ `TestApplyPoolEnv`),
> `secret-patch.yaml` is gone, `app-patch.yaml` sets the env; (2) **post placement** in `seedbench.go` follows A's rule
> (50 % into the first three spaces, 50 % uniform — round-robin had put 533 posts into private spaces vs A's 266,
> shrinking B's public/search set by 20 %); `TestSeedBenchmarkShape` asserts it; (3) the bench overlay runs
> **`postgres:18` (Debian, uid 999)** with A's probe timing instead of `18-alpine` — same image on both sides.
> Methodology and results: `thesis/results/AB-COMPARISON-v1.md`.

---

## 1. The fairness rules in one page (why these changes and not others)

The thesis compares two **systems** (SPA + JSON API vs. SSR monolith) at the **system level**. It is
not a framework micro-benchmark. Hypotheses: **H1** resource use per served user journey; **H2** the
throughput ceiling where journey p95 first exceeds 500 ms on an identical dataset, journey mix and
cluster resources; **H3** dependency surface, CVEs, complexity and local dev-loop time.

Therefore the comparison deliberately **keeps** every paradigm difference as the subject of study:
HTML vs. JSON, sqlc vs. EF Core, int64 vs. ULID, `pg_trgm ILIKE` vs. `tsvector`, Redis pub/sub vs.
RabbitMQ + outbox, server-ingest vs. presigned uploads, cookie sessions vs. JWT. **Nobody rewrites
features to look alike.**

What *must* be equal (the fairness invariants), and what in B currently violates them:

| # | Invariant | B today (verified) | Change |
|---|---|---|---|
| F1 | Same functional work per measured journey | Feed page = 8, search page = 10 (A: 20) — `handlers_feed.go:16`, `handlers_search.go:20`. Every new post synchronously writes a notification row + unread count + 2 toast renders + bus publish **per friend** (`notifications.go:58-70`, `live.go:234`) — A has no equivalent. | B-01, B-02 |
| F2 | Same dataset volumes **and** distribution | Volumes match. Distributions differ (1 pinned vs 1 %, 25 %-on-1 % vs squared-uniform comments, 12000/3000 votes incl. 10 % downvotes vs 11250/3750 likes, 10 fixed sentences vs 31-word bank, 2 vs 4 private spaces). | record now (meta.json); B-09 later |
| F3 | Same resources | App `100m/128Mi → 1/512Mi`, `replicas: 3`, HPA 2–10 @ 60 % (`app.yaml:51,162-168`, `hpa.yaml:16-24`); Postgres `100m/128Mi → 1/512Mi`, **zero** `postgresql.conf` tuning (`postgres.yaml:86-92`); probes have no `timeoutSeconds` (1 s default, `app.yaml:145-156`); pgxpool at default `max(4, NumCPU)` (`postgres.go:40-52`, pgx v5.11.0 `pgxpool/pool.go:19,384-387`). | B-05 (bench overlay) |
| F4 | Same load harness | Your `app/forum/loadtest/forum.ts` and A's `load/k6/main.js` express different behaviour (see §4 K1 for the three defects we found in `forum.ts`). | **one** shared harness (lives in A's repo, drives B through a target adapter — nothing for you to write) |
| F5 | Same security posture on measured paths | OAuth handlers log in a **mock user** whenever client credentials are unset (`handlers_oauth.go:131-137,145-151,159-164`) — live in every deployment without OAuth env. | B-03 |
| F6 | No cache tier on one side only | B's Redis is a pub/sub bus, not a cache — fine. A's planned Redis stays out until after measurements. | nothing |
| F7 | Compression cost in the same place | B gzips in-process unconditionally (`router.go:56` `middleware.Compress(5)`); A gzips at ingress-nginx (cluster-wide ConfigMap). | B-04 |
| F8 | Tracing/logging parity during measured runs | B samples 100 % of requests to Tempo when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (`tracing.go:150-158`); one Info log line per request (`logging.go:95-118`). A: 100 % tracing too, one Info line per request. | both OFF tracing for runs (env only, B-05); logs stay |

Everything else that differs is **documented, not leveled** (§6).

---

## 2. Decision summary — the golden mean

| Feature / aspect | A | B | Decision | Reason |
|---|---|---|---|---|
| Spaces, threads, comments (depth 5), like/upvote, pinned float, search, profile, friends, 1:1 DM, friend-request notification, live push of new thread/comment | yes | yes | **Core** — measured journeys use only this | shared surface |
| Global ("home") feed | missing (SPA merges per-category feeds client-side) | yes (`GET /`) | J1 = **single-space feed on both** for now; A adds a global feed later (P1 on A) | cheapest honest J1 today |
| Page size | 20 | 8 / 10 | **B → 20** (B-01) | F1 |
| Friend fan-out on new post | none | durable notification + push per friend | **B: env flag, off in bench** (B-02); feature stays in code | F1; it is outside the spec core (§3.6 lists only friend-request notifications) |
| Push on reaction | yes (WS) | no | document; **do not add** to B | not a measured journey; adding = scope creep |
| Comment delete | tombstone `[deleted]` | hard delete + cascade | document (OQ-1) | not measured |
| Upload | presigned PUT + commit | multipart `POST /uploads` → **202** + River + `/uploads/{id}/status` | document; harness polls (P1) | paradigm |
| WS fragments rendered per locale (EN+PL) | n/a | yes, 2× per broadcast | leave, document | microseconds; J8 is a hold, not a render benchmark |
| OAuth mock login | n/a | yes when creds unset | **gate behind `GOMX_OAUTH_MOCK=1`** (B-03) | security; trivy/Sonar day |
| Compression | ingress | in-app | **B: `GOMX_COMPRESS=0` in bench** (B-04); ingress gzips both | F7 |
| TLS | https://forum.local, Secure cookies | plain http, `GOMX_SECURE_COOKIES=false` | **B behind TLS too** (`gomx.local` SAN from Jakub's mkcert; overlay ingress TLS; `GOMX_SECURE_COOKIES=1`) | equal transport + cookie posture; ~0 cost on B's side |
| Replicas / HPA | HPA 1–3 @ 70 % | HPA 2–10 @ 60 %, replicas 3 | **Day 0: fixed 2 replicas, no HPA, on both.** Later: HPA 1–3 @ 70 % with A's behaviour block on both | removes autoscaler noise from the first numbers |
| App resources | 150m/256Mi → 750m/512Mi | 100m/128Mi → 1/512Mi | **both → requests 250m/256Mi, limits 1000m/512Mi** | F3 |
| Postgres | `postgres:18`, `shared_buffers=256MB effective_cache_size=512MB max_connections=100`, 250m/512Mi → 1000m/1Gi | `postgres:18-alpine`, defaults, 100m/128Mi → 1/512Mi | **B copies A's args + limits** (overlay) | F3 |
| DB pool | Npgsql `Maximum Pool Size=30` | pgxpool default (8 on this node, see B-05a) | **B: `GOMX_DB_MAX_CONNS=30`** (code + overlay env; a `pool_max_conns` URL param crashes startup) | F3 |
| Rate limiters | raised to 1 000 000 for runs, recorded | auth 10/min/IP | **B: `GOMX_AUTH_RATELIMIT=0`** in bench overlay, recorded | F5 (recorded) |
| Tracing | 100 % → Tempo | 100 % → Tempo | **OFF on both during measured runs** | one variable fewer; Tempo OOM risk |
| Dataset distribution (D2–D6) | reference | differs | **record for Day 0; B adopts A's generators later** (B-09, P1) | B's seed is index-driven and small; porting ~2 h |
| Social seed | 0 rows in Benchmark | 2 400 / 80 / 2 000 | **A adopts B's numbers** (P1 on A) | needed for J9 |
| Downvotes | none | 10 % of seeded votes | **B → 0 % in bench seed** (part of B-09) | reaction distribution parity |
| Features outside core (B: downvote, sorts/ranges/density, i18n PL, themes, SEO, OAuth+email, voice, video, share-to-DM, DM reply-to, typing, read receipts, OS notifications, review comments, Android, templbook, GoF patterns) | — | keep | **keep all; describe qualitatively**; excluded from "core scope" static metrics where generated | nothing here changes a measured number |
| Features outside core (A: groups + group chat, blocks, privacy, presence, tags, ACL/roles/admin API) | keep | — | same | same |
| Physical cuts | none | none | **none at P0** | no measured-path impact found that a flag doesn't solve |

---

## 3. Work items

### P0 — needed for tomorrow

#### B-01 — Feed and search page size → 20

- **Invariant:** F1 (same work per journey). A serves 20 items per feed/search page; B serves 8/10,
  so a B "page" is 40–50 % of an A "page" — journeys/sec would be meaningless.
- **Current code:**
  - `app/forum/web/handlers_feed.go:16` — `const feedPageSize = 8`; used at `:29`, `:65`, `:79-80`,
    `app/forum/web/handlers_community.go:25,82,100-101`, `app/forum/web/pages.templ:176` (masonry
    `order:` offset only).
  - `app/forum/web/handlers_search.go:20` — `searchPageSize = 10` (`:21-22` also define
    `searchListCap = 25`, `communitySuggestLimit = 8`); used at `:59`, `:62`.
- **Change:**
  ```go
  // app/forum/web/handlers_feed.go
  -const feedPageSize = 8
  +// feedPageSize is pinned by the A/B benchmark contract (forum-spec §6: page size 20 on both sides).
  +const feedPageSize = 20
  ```
  ```go
  // app/forum/web/handlers_search.go
  -	searchPageSize        = 10
  +	searchPageSize        = 20 // A/B contract (forum-spec §6)
  ```
  `searchListCap = 25` is a cap on the *users/communities* list variants — leave it.
- **Tests:** none pin 8 or 10 (`grep -rn "feedPageSize\|searchPageSize" --include=*_test.go .` → no
  hits; `handlers_feed_test.go:17 TestIndexAndThreadRender` asserts only 200). Playwright specs
  contain no `page=2`/`cursor` assertions. Optional guard so it cannot regress silently:
  ```go
  // app/forum/web/handlers_feed_test.go
  func TestFeedPageSizeMatchesABContract(t *testing.T) {
  	t.Parallel()
  	if feedPageSize != 20 || searchPageSize != 20 {
  		t.Fatalf("feedPageSize=%d searchPageSize=%d; A/B contract pins both to 20", feedPageSize, searchPageSize)
  	}
  }
  ```
- **Verification:**
  ```bash
  go test ./app/forum/web/...
  PORT=8091 GOMX_SEED=bench DATABASE_URL=postgres://gomx:gomx@localhost:5432/gomx go run ./cmd/server/main.go &
  curl -s http://localhost:8091/c/general | grep -o 'id="post-[0-9]*"' | wc -l      # expect 20
  curl -s 'http://localhost:8091/search?q=benchmark' | grep -o 'id="post-[0-9]*"' | wc -l   # expect 20
  ```
- **Acceptance:** both counts print 20; `task lint` clean.
- **Effort:** 15 min. **Risk:** the 2-column masonry split (`pages.templ:176`) uses `feedPageSize`
  for CSS `order:` — a visual check of `/` is enough.

#### B-02 — Friend fan-out on new post behind an env flag (off in bench)

- **Invariant:** F1. On every thread create B runs `RecipientsForPost` (`ListFriends` + `GetCommunity`
  + per-friend `GetMembership` for private spaces — `app/forum/notify/service.go:55-76`) and then,
  **per friend**, one INSERT + one unread COUNT + two templ renders (EN/PL toast) + a bus publish
  (`app/forum/web/notifications.go:30-52`), synchronously before the 303. The bench seed gives every
  user ~6 friends (`seedbench.go:306-326`), i.e. ≈12+ extra queries per J4 on B. A's thread create
  writes one outbox row. The feature is outside the spec core (§3.6 names only friend-request
  notifications), so it is flagged, not removed.
- **Current code:** `app/forum/web/notifications.go:58-70` `notifyFriendsOfPost`, called from
  `app/forum/web/live.go:234` inside `broadcastPost`. Server wiring: `app/forum/web/server.go:45-62`
  (`Server` struct), `:64-74` (`ServerConfig`), builder `app/forum/web/server_builder.go:24-80`,
  composition root `internal/server/forum.go:53-62`.
- **Change (follow the existing `GOMX_SECURE_COOKIES` truthy-parser style, `session.go:147-154`):**
  ```go
  // app/forum/web/server.go — Server struct (after `bus EventBus`)
  +	// friendPostNotifications gates notifyFriendsOfPost. Default true; the A/B
  +	// benchmark overlay sets GOMX_NOTIFY_FRIENDS_ON_POST=0 because A has no
  +	// per-friend fan-out on thread create (forum-spec §3.6 core = friend-request
  +	// notifications only).
  +	friendPostNotifications bool
  ```
  ```go
  // app/forum/web/server.go — ServerConfig
  +	FriendPostNotifications *bool // nil => true
  ```
  ```go
  // app/forum/web/server.go — NewServer
  +	s.friendPostNotifications = cfg.FriendPostNotifications == nil || *cfg.FriendPostNotifications
  ```
  ```go
  // app/forum/web/server_builder.go
  +func (b *ServerBuilder) WithFriendPostNotifications(enabled bool) *ServerBuilder {
  +	b.cfg.FriendPostNotifications = &enabled
  +	return b
  +}
  ```
  ```go
  // app/forum/web/notifications.go
   func (s *Server) notifyFriendsOfPost(ctx context.Context, p shared.Post) {
  -	if s.notifySvc == nil {
  +	if s.notifySvc == nil || !s.friendPostNotifications {
   		return
   	}
  ```
  ```go
  // internal/server/forum.go — next to allowedExtraOrigins()
  +// friendPostNotificationsEnabled reads GOMX_NOTIFY_FRIENDS_ON_POST (default on).
  +// "0", "false", "no", "off" disable the per-friend fan-out on thread create.
  +func friendPostNotificationsEnabled() bool {
  +	switch strings.ToLower(strings.TrimSpace(os.Getenv("GOMX_NOTIFY_FRIENDS_ON_POST"))) {
  +	case "0", "false", "no", "off":
  +		return false
  +	default:
  +		return true
  +	}
  +}
  ```
  and in the builder chain (`forum.go:53-62`): `.WithFriendPostNotifications(friendPostNotificationsEnabled()).`
- **Tests:** `app/forum/web/notifications_test.go:50 TestNotifyFriendsOfPost` keeps passing (default
  true via `newTestServer`, `web_test.go:44-77`). Add:
  ```go
  func TestNotifyFriendsOfPostDisabled(t *testing.T) {
  	t.Parallel()
  	srv, _ := newTestServer(t)
  	srv.friendPostNotifications = false
  	ctx := context.Background()
  	author := mustTestAccount(t, srv, "writer2", "Writer")
  	friend := mustTestAccount(t, srv, "reader2", "Reader")
  	if err := srv.store.SendFriendRequest(ctx, author.ID, friend.ID); err != nil { t.Fatal(err) }
  	if err := srv.store.AcceptFriendRequest(ctx, friend.ID, author.ID); err != nil { t.Fatal(err) }
  	pub, err := srv.store.CreateCommunity(ctx, shared.Community{Slug: "town2", Name: "Town", Visibility: shared.VisibilityPublic, OwnerID: author.ID})
  	if err != nil { t.Fatal(err) }
  	post := srv.store.AddPost(ctx, shared.Post{Title: "Hi", Body: "body", Author: author, CommunityID: pub.ID})
  	post.CommunitySlug = pub.Slug
  	srv.notifyFriendsOfPost(ctx, post)
  	if n := srv.notifySvc.UnreadCount(ctx, friend.ID); n != 0 {
  		t.Fatalf("fan-out disabled, friend unread = %d, want 0", n)
  	}
  }
  ```
- **Verification:** `go test ./app/forum/web/ -run 'TestNotifyFriendsOfPost' -v` → both PASS.
- **Acceptance:** with `GOMX_NOTIFY_FRIENDS_ON_POST=0` a thread create produces **zero** rows in
  `notifications`; default behaviour unchanged.
- **Effort:** 30–45 min. **Risk:** none for production (default on).

#### B-03 — OAuth mock login only with an explicit opt-in

- **Invariant:** F5 (same security posture) and H3 scan hygiene: today `GET /auth/google` or
  `/auth/facebook` on **any** deployment without OAuth env vars creates/logs in
  `mock.<provider>.user@example.com` (`handlers_oauth.go:131-137,145-151` → `:159-164`
  `handleMockOAuthLogin`). That is an unauthenticated login as a fixed account — it must not be
  reachable in the benchmark cluster (or anywhere but local dev).
- **Change:**
  ```go
  // app/forum/web/handlers_oauth.go — near googleOAuthConfig/metaOAuthConfig (:29-48)
  +// oauthMockEnabled gates the credential-less mock login (dev convenience only).
  +// It must be opted into explicitly: GOMX_OAUTH_MOCK=1.
  +func oauthMockEnabled() bool {
  +	switch strings.ToLower(strings.TrimSpace(os.Getenv("GOMX_OAUTH_MOCK"))) {
  +	case "1", "true", "yes", "on":
  +		return true
  +	default:
  +		return false
  +	}
  +}
  ```
  ```go
   func (s *Server) handleGoogleLogin(w http.ResponseWriter, r *http.Request) {
   	cfg := s.googleOAuthConfig(r)
   	if cfg.ClientID == "" || cfg.ClientSecret == "" {
  -		slog.Info("Google OAuth credentials not configured; logging in with mock user")
  -		s.handleMockOAuthLogin(w, r, "google")
  +		if !oauthMockEnabled() {
  +			slog.Warn("Google OAuth not configured and GOMX_OAUTH_MOCK unset; refusing login")
  +			http.Redirect(w, r, path(r.Context(), routeLogin)+"?error=oauth_unavailable", http.StatusSeeOther)
  +			return
  +		}
  +		slog.Info("Google OAuth credentials not configured; logging in with mock user (GOMX_OAUTH_MOCK=1)")
  +		s.handleMockOAuthLogin(w, r, "google")
   		return
   	}
  ```
  Same block in `handleFacebookLogin` (`:145-151`). If `path(ctx, …)` is not in scope in this file,
  redirect to `"/login"` as the other handlers do. If you prefer to surface the error in the auth
  form, map `oauth_unavailable` through `i18n_errors.go` (localization skill applies — both TOML
  catalogs).
- **Tests:** none exist for OAuth (`grep -rln -i "oauth" --include=*_test.go .` → empty). Add to
  `app/forum/web/handlers_auth_test.go` (or a new `handlers_oauth_test.go`), same `newTestServer`
  style:
  ```go
  func TestOAuthMockRequiresOptIn(t *testing.T) {
  	_, base := newTestServer(t)
  	t.Setenv("GOMX_OAUTH_MOCK", "")                   // not parallel: t.Setenv forbids t.Parallel
  	c := &http.Client{CheckRedirect: func(*http.Request, []*http.Request) error { return http.ErrUseLastResponse }}
  	res, err := c.Get(base + "/auth/google")
  	if err != nil { t.Fatal(err) }
  	defer res.Body.Close()
  	if res.StatusCode != http.StatusSeeOther || !strings.Contains(res.Header.Get("Location"), "/login") {
  		t.Fatalf("status=%d location=%q; want 303 to /login", res.StatusCode, res.Header.Get("Location"))
  	}
  	if len(res.Cookies()) != 0 && res.Cookies()[0].Name == "forum_session" {
  		t.Fatal("mock login must not mint a session without GOMX_OAUTH_MOCK=1")
  	}
  }
  ```
- **Verification:** `go test ./app/forum/web/ -run TestOAuthMockRequiresOptIn -v`;
  `curl -si http://localhost:8091/auth/google | head -3` → `303` to `/login…`, **no**
  `Set-Cookie: forum_session`.
- **Acceptance:** without `GOMX_OAUTH_MOCK=1` no session is created from `/auth/*`; `task dev`
  developers who rely on the mock set `GOMX_OAUTH_MOCK=1` (add it to `.env.example`/SETUP.md).
- **Effort:** 30 min. **Risk:** e2e specs that depended on the mock (none found) would need the env.

#### B-04 — In-app gzip behind an env flag (off in bench; ingress compresses both)

- **Invariant:** F7. A compresses at ingress-nginx (cluster ConfigMap `use-gzip`, applies to every
  Ingress in the cluster, including yours). If B also compresses in-process, B pays gzip CPU inside
  the measured pod and A does not. nginx will not re-compress an already `Content-Encoding: gzip`
  upstream response, so the only effect of leaving `Compress(5)` on is unilateral cost on B.
- **Current code:** `internal/server/router.go:56` `r.Use(middleware.Compress(5))` — unconditional.
- **Change (mirrors `beaconRateLimiter`'s env parsing at `router.go:155-167`):**
  ```go
  // internal/server/router.go
  -	r.Use(middleware.Compress(5))
  +	// GOMX_COMPRESS=0 disables in-process gzip when a reverse proxy (ingress-nginx in the
  +	// A/B benchmark cluster) compresses responses for every upstream — otherwise B would
  +	// pay compression CPU inside the measured pod while A does not. Default: on.
  +	if compressEnabled() {
  +		r.Use(middleware.Compress(5))
  +	}
  ```
  ```go
  +func compressEnabled() bool {
  +	switch strings.ToLower(strings.TrimSpace(os.Getenv("GOMX_COMPRESS"))) {
  +	case "0", "false", "no", "off":
  +		return false
  +	default:
  +		return true
  +	}
  +}
  ```
- **Tests:** add to `internal/server/router_test.go` (if absent, create; `internal/server/csrf_test.go`
  shows the package's test style): with `t.Setenv("GOMX_COMPRESS","0")` a `GET /healthz` with
  `Accept-Encoding: gzip` has **no** `Content-Encoding` header; default run has it for a >1 KiB
  text body (use `/static/styles.css` or a page).
- **Verification:**
  ```bash
  GOMX_COMPRESS=0 PORT=8091 go run ./cmd/server/main.go &
  curl -sI -H 'Accept-Encoding: gzip' http://localhost:8091/ | grep -i content-encoding   # expect nothing
  ```
- **Effort:** 20 min.

#### B-05 — Benchmark kustomize overlay `deploy/k8s/bench/`

- **Invariant:** F3 (same resources), F5 (recorded limiter), F8 (tracing off), plus two operational
  defects that would silently ruin runs (probe timeout, seed race/liveness kill — see Risks).
- **Target cluster:** Jakub's minikube profile `forum` — **docker** driver, calico CNI, `ingress` +
  `metrics-server` addons, 8 CPU / 10 GiB (`.env` of forum-dotnet). Your manifests were checked
  against it: no hostPath/privileged/NodePort/storageClass pins; all pods `runAsNonRoot`,
  `seccompProfile: RuntimeDefault`, `drop ALL`, `readOnlyRootFilesystem` — they deploy unchanged
  (`deploy/k8s/*.yaml`). Your `allow-gomx-app` NetworkPolicy already admits `ingress-nginx` and
  `monitoring` namespaces (`networkpolicy.yaml:36-47`) — both names match Jakub's cluster. The image
  is built with `docker build -f ContainerFile.dev` straight into minikube's docker daemon
  (`eval $(minikube -p forum docker-env)`), so `imagePullPolicy: IfNotPresent` (`app.yaml:97`) works
  without a registry. **Only Jakub's monitoring stack is installed** (two kube-prometheus-stacks in
  one `monitoring` namespace would fight over CRDs/operators); his Prometheus selects ServiceMonitors
  from all namespaces, so your `deploy/monitoring/servicemonitor.yaml` is applied as-is (A-side task).
- **Current values being overridden:** `app.yaml:51` `replicas: 3`; `:111-139` env (incl.
  `OTEL_EXPORTER_OTLP_ENDPOINT: http://tempo.monitoring.svc:4318`, `GOMX_SECURE_COOKIES: "false"`);
  `:145-156` probes without `timeoutSeconds`; `:162-168` resources; `hpa.yaml` 2–10 @ 60 %;
  `postgres.yaml:49` `postgres:18-alpine`, no `args`, `:86-92` resources; `secret.yaml:19`
  `DATABASE_URL=…?sslmode=disable`; `ingress.yaml` no TLS.
- **New files (complete):**

  `deploy/k8s/bench/kustomization.yaml`
  ```yaml
  # A/B benchmark overlay (thesis, 2026-10). Deploys gomx into Jakub's minikube profile `forum`
  # next to Architecture A with the COMMON resource profile both sides agreed on:
  #   app: requests 250m/256Mi, limits 1000m/512Mi, FIXED 2 replicas, no HPA (Day 0);
  #   postgres: 18, shared_buffers=256MB, effective_cache_size=512MB, max_connections=100,
  #             requests 250m/512Mi, limits 1000m/1Gi; pgxpool max 30;
  #   tracing OFF, in-app gzip OFF (ingress compresses both), auth limiter OFF (recorded),
  #   friend fan-out on post OFF, bench seed, TLS via gomx.local.
  # Apply:  kubectl apply -k deploy/k8s/bench   (image: gomx:bench — see README in this dir)
  apiVersion: kustomize.config.k8s.io/v1beta1
  kind: Kustomization
  resources:
    - ../
  images:
    - name: gomx
      newTag: bench
  patches:
    - path: app-patch.yaml
    - path: hpa-delete.yaml
    - path: postgres-patch.yaml
    - path: ingress-patch.yaml
  ```

  `deploy/k8s/bench/app-patch.yaml`
  ```yaml
  apiVersion: apps/v1
  kind: Deployment
  metadata:
    name: gomx
    namespace: gomx
  spec:
    # 1 for the FIRST boot against an empty DB: the bench seed runs in-process at startup
    # (internal/server/forum.go selectStore → seedStore) with NO lock around the IsEmpty→seed
    # window, so two pods would both seed and both hit `username_lc UNIQUE` → crash-loop with a
    # half-seeded DB. Scale to 2 once the log line "benchmark seed complete" appears.
    replicas: 1
    template:
      spec:
        containers:
          - name: gomx
            resources:
              requests: { cpu: 250m, memory: 256Mi }
              limits:   { cpu: "1",  memory: 512Mi }
            # The bench seed hashes 800 PBKDF2 accounts + ~27k row inserts BEFORE the HTTP
            # listener starts (cmd/server/main.go: NewRouter→buildForum seeds, httpserver.Run
            # listens afterwards). With the base liveness (initialDelay 5 s, period 10 s,
            # failureThreshold 3) the kubelet kills the pod ~35 s into seeding, the restart sees
            # IsEmpty=false and skips the seed → PARTIAL dataset. A startupProbe holds liveness
            # off until the listener is up (up to 15 min).
            startupProbe:
              httpGet: { path: /healthz, port: http }
              periodSeconds: 10
              failureThreshold: 90
            # timeoutSeconds 5 (k8s default 1 s): a saturated pod that misses a 1 s deadline flaps
            # unready, ingress withdraws its endpoint and (with an HPA) its CPU samples are
            # discarded — found live on A during Phase 9c. Same value on both sides.
            livenessProbe:
              httpGet: { path: /healthz, port: http }
              initialDelaySeconds: 5
              periodSeconds: 10
              timeoutSeconds: 5
            readinessProbe:
              httpGet: { path: /readyz, port: http }
              initialDelaySeconds: 3
              periodSeconds: 5
              timeoutSeconds: 5
            # NOTE: keep comments on their own lines — kustomize v5.8.1's strategic merge fails on trailing
            # comments after flow mappings ("yaml: line N: could not find expected ':'", found live 2026-10-17).
            #   GOMX_AUTH_RATELIMIT=0  recorded in meta.json (F5) · GOMX_COMPRESS=0  B-04 · GOMX_NOTIFY_FRIENDS_ON_POST=0  B-02
            #   GOMX_DB_MAX_CONNS=30  B-05a · GOMX_SECURE_COOKIES=1  TLS ingress · OTEL_EXPORTER_OTLP_ENDPOINT="" = unset → off
            env:
              - { name: GOMX_SEED, value: "bench" }
              - { name: GOMX_AUTH_RATELIMIT, value: "0" }
              - { name: GOMX_BEACON_RATELIMIT, value: "0" }
              - { name: GOMX_COMPRESS, value: "0" }
              - { name: GOMX_NOTIFY_FRIENDS_ON_POST, value: "0" }
              - { name: GOMX_DB_MAX_CONNS, value: "30" }
              - { name: GOMX_SECURE_COOKIES, value: "1" }
              - { name: GOMX_ALLOWED_ORIGINS, value: "gomx.local" }
              - { name: OTEL_EXPORTER_OTLP_ENDPOINT, value: "" }
              - { name: GOMX_ENV, value: "benchmark" }
  ```

  `deploy/k8s/bench/hpa-delete.yaml`
  ```yaml
  # Day 0 runs with FIXED replicas on both sides (autoscaler noise out of the first numbers).
  # Later classes re-enable an identical HPA (1–3 @ 70 %, A's behaviour block) on both.
  $patch: delete
  apiVersion: autoscaling/v2
  kind: HorizontalPodAutoscaler
  metadata:
    name: gomx
    namespace: gomx
  ```

  `deploy/k8s/bench/postgres-patch.yaml`
  ```yaml
  apiVersion: apps/v1
  kind: StatefulSet
  metadata:
    name: postgres
    namespace: gomx
  spec:
    template:
      spec:
        containers:
          - name: postgres
            # Identical to A's k8s/postgres/statefulset.yaml:31 (nothing else is tuned on A).
            args: ["-c", "shared_buffers=256MB", "-c", "effective_cache_size=512MB", "-c", "max_connections=100"]
            resources:
              requests: { cpu: 250m, memory: 512Mi }
              limits:   { cpu: "1",  memory: 1Gi }
  ```

  **No `secret-patch.yaml`.** The first draft of this document put `pool_max_conns=30` into
  `DATABASE_URL`. That **breaks startup** (verified live 2026-10-17 against the built image):
  `runMigrations` opens the *raw* DSN through `database/sql` + pgx stdlib (`postgres.go:92-95`
  `sql.Open("pgx", dsn)`), which forwards `pool_max_conns` to the server as a runtime parameter →
  `FATAL: unrecognized configuration parameter "pool_max_conns" (SQLSTATE 42704)`. Only
  `pgxpool.ParseConfig` strips the `pool_*` keys. Hence the pool size is set in code via an env var
  (B-05a below) and the overlay adds `GOMX_DB_MAX_CONNS: "30"` to the app env (already listed in
  `app-patch.yaml`).

  `deploy/k8s/bench/ingress-patch.yaml`
  ```yaml
  apiVersion: networking.k8s.io/v1
  kind: Ingress
  metadata:
    name: gomx
    namespace: gomx
    annotations:
      nginx.ingress.kubernetes.io/ssl-redirect: "true"
  spec:
    tls:
      - hosts: [gomx.local]
        secretName: gomx-tls   # created by Jakub from his mkcert cert (gomx.local SAN) — not in git
  ```

  `deploy/k8s/bench/README.md` — 10 lines: what the overlay is, the first-boot (replicas 1 →
  scale 2) procedure, the reset procedure (below), and that `gomx-tls` is created out-of-band.

- **Reset procedure (between benchmark days, both sides reseed):**
  ```bash
  kubectl -n gomx scale deploy/gomx --replicas=0
  kubectl -n gomx exec postgres-0 -- psql -U gomx -d postgres -c "DROP DATABASE gomx;" -c "CREATE DATABASE gomx OWNER gomx;"
  kubectl -n gomx scale deploy/gomx --replicas=1     # seeds; wait for "benchmark seed complete"
  kubectl -n gomx logs deploy/gomx -f | grep -m1 'benchmark seed complete'
  kubectl -n gomx scale deploy/gomx --replicas=2
  ```
- **Verification (Jakub runs on the cluster; you can validate rendering locally):**
  ```bash
  kubectl kustomize deploy/k8s/bench > /tmp/gomx-bench.yaml      # renders without error
  grep -c 'kind: HorizontalPodAutoscaler' /tmp/gomx-bench.yaml   # 0
  grep -A1 'image: gomx' /tmp/gomx-bench.yaml                    # gomx:bench
  grep 'GOMX_DB_MAX_CONNS' /tmp/gomx-bench.yaml                  # present
  ```
  (`task k8s:lint` uses podman for kubeconform/trivy; the plain `kubectl kustomize` render is enough.)
- **Acceptance:** renders; after apply on the shared cluster: 1 pod seeds to 800/12/1600/9000 (check
  `curl -sk https://gomx.local/stats --resolve gomx.local:443:$(minikube -p forum ip)` →
  `{"posts":1600,"comments":9000,…}`), then 2 Ready pods; `GET /healthz` 200; no HPA.
- **Effort:** 1–1.5 h. **Risks/pitfalls:** (0) An identical copy of this overlay is already tested end-to-end on the A side (`forum-dotnet/k8s/gomx-bench/`, rendered against your base @ `8b23bc1`) — copy it if you prefer; the only difference is that A pre-renders your base (kustomize refuses outside-root bases). (1) `images:` transformer matches the *name* `gomx` only —
  keep `image: gomx:dev` in the base. (2) Re-applying the overlay later snaps `replicas` back to 1;
  scale to 2 again (or land B-08 and raise the overlay to 2). (3) Deleting the `gomx` namespace does
  **not** wipe minikube hostpath PVC data (`/tmp/hostpath-provisioner/gomx/…` inside the VM) — the
  DROP DATABASE reset above is the reliable path. (4) `GOMX_SECURE_COOKIES=1` + plain-HTTP access
  (port-forward) makes browsers drop the cookie; use `https://gomx.local` or set it `"false"` for
  port-forward debugging.

#### B-05a — Pool size via `GOMX_DB_MAX_CONNS` (pgxpool `MaxConns`)

- **Invariant:** F3 — A pins Npgsql `Maximum Pool Size=30`; B runs at pgxpool's default
  `max(4, runtime.NumCPU())` = 8 on the bench node (`pgxpool/pool.go:19,384-387`, pgx v5.11.0).
- **Why not the URL:** see B-05 — `runMigrations` passes the raw DSN to `database/sql`
  (`app/forum/shared/postgres/postgres.go:92-95`), so `pool_max_conns` in `DATABASE_URL` is a startup
  crash. (Alternative if you prefer URL params: open goose's connection from the parsed config,
  `sql.OpenDB(stdlib.GetConnector(*cfg.ConnConfig))` — `cfg.ConnConfig` has the `pool_*` keys removed.
  The env var is the smaller change.)
- **Change** (`app/forum/shared/postgres/postgres.go`, in `New` right after `pgxpool.ParseConfig`, `:40-43`;
  add `"strconv"` to the imports):
  ```go
  	cfg, err := pgxpool.ParseConfig(dsn)
  	if err != nil {
  		return nil, fmt.Errorf("parse dsn: %w", err)
  	}
  +	// GOMX_DB_MAX_CONNS caps the pool (default: pgxpool's max(4, NumCPU)). The A/B benchmark pins
  +	// both sides to 30 per replica (A: Npgsql "Maximum Pool Size=30"); 3 replicas × 30 < max_connections=100.
  +	// Set here rather than as a pool_max_conns URL param because runMigrations hands the raw DSN to
  +	// database/sql, which would forward pool_* keys to the server as (unknown) runtime parameters.
  +	if v := strings.TrimSpace(os.Getenv("GOMX_DB_MAX_CONNS")); v != "" {
  +		if n, err := strconv.Atoi(v); err == nil && n > 0 {
  +			cfg.MaxConns = int32(n)
  +		}
  +	}
  ```
  River shares this pool (`internal/server/forum.go:75`) — unchanged.
- **Tests:** unit test in `app/forum/shared/postgres/postgres_test.go` (new, no DB needed): with
  `t.Setenv("GOMX_DB_MAX_CONNS","30")` a helper that applies the env to a parsed config yields
  `cfg.MaxConns == 30`; with it unset the default stays. (Factor the env application into
  `applyPoolEnv(cfg *pgxpool.Config)` to make it testable.)
- **Verification:** start with `GOMX_DB_MAX_CONNS=30` and run
  `psql … -c "select count(*) from pg_stat_activity where datname='gomx'"` under load (≤ 30 + River + migrations).
- **Effort:** 20 min.

#### B-06 — Record the Day-0 dataset deviations (no code)

Nothing to build; Jakub's `meta.json` records them. Listed here so you know what the paper will
say about B's seed until B-09 lands: 1 pinned post (A: 1 % = 16); comments 25 % on 1 % of posts
(A: squared-uniform skew); 12 000 post votes incl. 10 % downvotes + 3 000 comment likes (A: 11 250
thread + 3 750 comment likes, no downvotes); bodies from 10 fixed sentences 0.3–1.2 KB (A: 31-word
bank, 120–320 words); private spaces 2 × ~200 members (A: 4 × 25); no blocked users / deleted
threads (A: 20 blocked, ~1 % deleted). Volumes 800/12/1600/9000/15000 match.

### P1 — next classes

#### B-07 — Startup probe + probe timeouts in the **base** manifests

Move the `startupProbe` and `timeoutSeconds: 5` from the overlay into `deploy/k8s/app.yaml:145-156`
so your own cluster gets the same protection (the base liveness kills a seeding pod today — see
B-05 risks). Also consider `--horizontal-pod-autoscaler`-independent correctness: a 1 s probe
timeout is the single most common cause of phantom unreadiness under load.

#### B-08 — Serialize the first-boot seed (advisory lock) — ~1 h

- **Why:** `internal/server/forum.go:112-114` `if empty, err := pg.IsEmpty(ctx); err == nil && empty { seedStore(pg, blobs) }`
  runs *after* `runMigrations` released `pg_advisory_lock(7462839174)`
  (`app/forum/shared/postgres/postgres.go:99-105`). With `replicas: 3` on an empty DB all pods seed
  concurrently; losers `panic("bench seed account …")` (`seedbench.go:110-112`) and the DB can end
  up half-seeded with `IsEmpty=false`.
- **Change:** take a second session-level advisory lock around the check-and-seed
  (`SELECT pg_advisory_lock(7462839175)` … seed … `pg_advisory_unlock`), re-check `IsEmpty` inside
  the lock, log `benchmark seed complete` with counts (already there, `forum.go:137-140`). With this
  in place the overlay can ship `replicas: 2` from the start.
- **Test:** store integration test in `app/forum/shared/postgres/` (runs under
  `task test:integration`): two goroutines call the seed entry; exactly 800 accounts result.
- **Acceptance:** `kubectl apply -k deploy/k8s/bench` with `replicas: 2` on an empty DB → exactly
  one "benchmark seed complete" line, both pods Ready, counts correct.

#### B-09 — Align seed distributions with A (D2–D6) — ~2–3 h

A's seeder is the reference shape (it is the one already measured and archived). Port these into
`app/forum/shared/seedbench.go`, keeping your index-driven determinism:

| Item | A (reference) | Your current code | Change |
|---|---|---|---|
| Pinned | `index % 100 == 0` → 16 posts (1 %) | post 1 only (`seedbench.go:196-200`) | pin every 100th |
| Comments over posts | squared-uniform skew: `idx = int(n * u * u)` with `u` deterministic in [0,1) (A `SeedDistribution.ZipfIndex`) | 25 % on 16 hot posts, rest uniform (`:212-231`) | replace with `hotIndex(i, n) = int(float64(n) * f * f)` where `f = frac(i * 0.6180339887)` (golden-ratio sequence — deterministic, uniform in [0,1)) |
| Comment depth | 60/25/10/4/1 % over depths 0–4 | 2 of 3 comments chain on the previous (`:239-245`) | pick depth by `i % 100` bands: `<60` → 0, `<85` → 1, `<95` → 2, `<99` → 3, else 4; parent = a random earlier comment of that post at depth−1 (fall back to top-level) |
| Reactions | 15 000 = 75 % thread / 25 % comment (11 250 / 3 750), targets via the same squared-uniform skew, **no downvotes** | 12 000 votes with 1-in-10 downvotes + 3 000 likes (`:270-300`) | `PostVotes: 11250, CommentLikes: 3750`, drop the `i%10 == 0` downvote arm in bench mode |
| Body text | 31-word bank (`SeedText.cs:12-18`), body = `"# <title>\n\nseeded "` + 120–320 words (`ContentSeeder.cs:120`) with `.` + blank line every 18 words (`SeedText.cs:27-30`); comments = `seeded` + 12 words (`ContentSeeder.cs:184`) | 10 fixed sentences, 1–3 paragraphs (`:380-411`) | embed the bank below; body length `120 + (i*37) % 201` words; sentence break every 18 words |
| Private spaces | 4 of 12, 25 members each | 2 of 12 (`:141-143`), ~200 members by `(u+0,5,9)%12` (`:155-170`) | 4 private (`bench9`–`bench12`), members = 25 fixed indices per private space; public spaces keep your 3-per-user rule |

A's word bank (verbatim from `backend/src/Modules/Content/Forum.Modules.Content/Infrastructure/Seeding/SeedText.cs:12-18`
— 32 entries, `"keyset"` listed twice, so 31 unique words; every A body also starts with the token
`seeded`):

```go
var benchWordBank = []string{
	"forum", "thread", "comment", "keyset", "pagination", "benchmark", "architecture", "module",
	"postgres", "rabbitmq", "outbox", "reaction", "moderator", "category", "markdown", "latency",
	"throughput", "cluster", "kubernetes", "observability", "trace", "metric", "cache", "index",
	"aggregate", "domain", "event", "handler", "query", "keyset", "cursor", "tsvector",
}
```
> Note for both sides (recorded as open decision OD-6 in the A document): with a 31-word bank and
> 120–320-word bodies **every** word appears in nearly every body, so J6 on both sides today is a
> "match everything, then rank/sort 1 600 rows" query. If we want J6 to measure selective search,
> both seeders should move to a larger bank (~300 words, Zipf-weighted). Until then, record it.

- **Tests:** extend `app/forum/shared/seedbench_test.go:14-24 TestSeedBenchmarkShape` with the new
  counts (pinned = `Posts/100`, likes split, 4 private, 25 members each, no negative votes).
- **Acceptance:** `SELECT count(*) FROM posts WHERE pinned` = 16; `SELECT count(*) FROM post_votes WHERE dir < 0` = 0;
  private communities = 4 with 25 active members each; `avg(length(body))` of posts ≈ 1.5–2 KB.

#### B-10 — Image provenance for `meta.json` — 30 min

`ContainerFile.dev:58-60` does not pass `-X main.version=<sha>` (so traces/logs say `version=dev`,
`cmd/server/main.go:18`). Add `ARG GIT_SHA=dev` + `-ldflags="-s -w -X main.version=${GIT_SHA}"`;
Jakub builds with `--build-arg GIT_SHA=$(git rev-parse --short HEAD)` and tags `gomx:bench`
(+ `gomx:bench-<sha>`). Also expose the version on `/stats` JSON (`handlers_ops.go:27-34`) so the
run metadata can read it from the running pod.

#### B-11 — Upload journey (J4 with image, 3 % of iterations): nothing to change

Your `POST /uploads` returns **202** `{"upload_id","status":"processing"}` on the Postgres+River+S3
path (`handlers_media.go:181-184`) and the harness polls `GET /uploads/{id}/status` until
`completed` (`:187-230`). Keep it — it is the paradigm. Only confirm the ingress `proxy-body-size: 6m`
stays (A's 68-byte PNG is far below).

### P2 — optional

- **B-12** PSS labels on the `gomx` namespace (`pod-security.kubernetes.io/enforce: restricted`) —
  your pods already comply; A's namespace is `restricted`. Cosmetic parity for the paper.
- **B-13** Request logger: skip `/healthz` and `/static/*` like `/readyz` (`internal/logging/logging.go:97`).
  A logs probes at Verbose. Not measured (k6 never fetches static assets), tiny log-volume difference.
- **B-14** Pin the kube-prometheus-stack chart version in `Taskfile.yml:447` for your own cluster
  (A pins 87.15.1). Not used in the shared cluster.
- **B-15** `/sitemap.xml` (`handlers_seo.go:32-47`) lists all 1 600 public threads in one unbounded
  query — fine for discovery, keep it out of any measured mix (it is).

---

## 4. Interface contract between the two sides (identical copy in the A document)

Everything the shared k6 harness (`forum-dotnet/load/k6/`, target adapter `targets/b.js`) relies
on from B. If any of this changes on your side, tell Jakub — the adapter must change with it.

### 4.1 Hosts, TLS, health
- Base URL in the shared cluster: `https://gomx.local` (ingress-nginx, TLS from Jakub's mkcert cert,
  `ssl-redirect` on). k6 pins `gomx.local` → minikube IP via its `hosts` option (no `/etc/hosts`).
- `GET /healthz` → 200 `ok` (static); `GET /readyz` → 200 when Postgres pings within 2 s, else 503
  (`handlers_ops.go:16-25`). `/metrics` is on port 9090, not routed by the ingress.
- `GET /stats` → `{"posts":N,"comments":N,"connections":N}` (`handlers_ops.go:27-34`) — used as the
  seed sentinel (`posts == 1600` before a run).
- Locale: `/` is EN, `/pl/...` is PL; no `Accept-Language` negotiation; every response sets
  `Set-Cookie: gomx_locale=en` — the harness stays on `/` and never sends that cookie back.

### 4.2 Seeded identities (bench profile, `GOMX_SEED=bench`)
- Users `benchuser0001` … `benchuser0800` (zero-padded, 1-based — `seedbench.go:99`), all with
  password `DemoPassword1` (`seed.go:16`), email `<username>@bench.gomx`. No staff/blocked bands; the
  owner of community *c* is user *c*. The harness logs in `benchuser0034` … (same index band as A's
  `bench_user_0034` …, 200 users for demo/stress, 10 for smoke).
- Communities: slugs `general, showcase, golang, design, help, random, bench7 … bench12`
  (`seedbench.go` `benchCommunities`); since B-09 the last four, `bench9`–`bench12`, are private with
  25 members each. Public memberships: user with 0-based index `u` is an active member of
  `(u+off) % 12` for `off ∈ {0,5,9}`, remapped to `(u+off) % 8` when that index is private — so the
  `off=0` slot is always a public membership. **Posting requires active membership**
  (`shared/rules.go CanPostCommunity`), commenting only requires view access. The harness posts into
  `targets/b.js postableSlug(u)` = that `off=0` space (J4 only — weight 0 in mix v1).
- Posts are placed like A's seeder since 2026-10-17 (`benchCommunityIndex`: 50 % into the first three
  spaces, 50 % uniform) — ≈330/339/332 in the hot spaces, ≈66 elsewhere, 263 in private spaces.
- Post ids are `BIGINT IDENTITY` in insertion order (post 1 = pinned welcome); account ids are **not**
  guaranteed to equal the username number (accounts are created concurrently, `seedbench.go:88-116`)
  — the harness never derives ids from usernames; it scrapes them.

### 4.3 Authentication and CSRF
- `POST /login`, form-encoded `username`, `password` (optional `next`). Success: **303** to `/`
  with `Set-Cookie: forum_session=<token>; Path=/; HttpOnly; SameSite=Lax[; Secure]`; failure:
  **422** (auth page). With `HX-Request: true` success is 200 + `HX-Redirect`. No CSRF token
  field, no prior GET needed (`handlers_auth.go:55-76`). Rate-limited 10/min/IP unless
  `GOMX_AUTH_RATELIMIT=0` (`server.go:246-264`) — the bench overlay sets 0.
- The harness sends the cookie explicitly as `Cookie: forum_session=<token>` on authenticated
  requests (one token per VU, all minted in k6 `setup()` at ≤ 4 logins/s).
- CSRF (`internal/server/csrf.go:27-36,54-64`): unsafe methods pass when `Origin` is **absent** or
  its host equals the request `Host` or is in `GOMX_ALLOWED_ORIGINS`. The harness sends no `Origin`
  header (A's side sends none either — header-equivalent). 403 `cross-origin request blocked` on
  mismatch.
- Session lookup = one SQL per cookie-bearing request (`session.go:73-82`, `queries.sql:34-41`); no
  cache. Documented paradigm difference (§6).

### 4.4 Read journeys (anonymous)
| Journey | Request(s) on B | Expect | Notes |
|---|---|---|---|
| **J1 space feed** | `GET /c/{slug}` ; 30 % of iterations then `GET /c/{slug}/feed?page=2&cursor=<cursor>` | 200, 200 | first page = full HTML page with 20 `<article id="post-N">`; the cursor is scraped from `<div id="feed-sentinel" … hx-get="/c/{slug}/feed?page=2&amp;cursor=…">` (`pages.templ:198-207`, `urls.go:122-132`) — HTML-unescape `&amp;`. `?page=` alone is **cosmetic**; without `cursor` the handler re-serves page 1 (`handlers_feed.go:74-77`). Cursor = base64url(JSON `{p,c,i[,v,m]}`) (`shared/cursor.go:12-26`), opaque to the harness. Default sort `newest`. |
| **J2 thread open** | `GET /thread/{id}` | 200 | one query for the whole comment set, no LIMIT (`queries.sql:140-152`); tree built in Go. Anonymous → no per-comment like lookups. |
| **J6 search** | `GET /search?q=<word>` | 200 | `ILIKE '%q%'` over title/body/category with pg_trgm, OFFSET paging `?page=N`, 20/page after B-01 (`handlers_search.go`, `SearchPostsPaged`). Since B-09 both seeders use A's word bank, so the harness sends ONE shared word list to both targets (`lib/assets.js SEARCH_WORDS`). |
| **J7 profile** | `GET /u/{numericId}` | 200 | id scraped from post-card author links `href="/u/{id}"` (`post.templ:29,34`, `urls.go:61`). 20 posts tab. |
| **nav — list spaces** | `GET /communities` | 200 | public communities as `href="/c/{slug}"` (`community.templ:52`); replaces A-only tag autocomplete in the mix. |

Private communities return **404** to non-members (`rules.go:218-227`); the harness uses public
slugs only (discovered from `/communities` anonymously).

### 4.5 Write journeys (authenticated)
| Journey | Request on B | Expect | Notes |
|---|---|---|---|
| **J3 comment** | `POST /posts/{id}/comments`, form `body` (no `parent_id` → top-level) | **303** `Location: /thread/{id}#comment-{newId}` | harness uses `redirects: 0` (a followed redirect would re-render the whole thread and double-count J2 work). Rejected → 303 `/thread/{id}` without `#comment-` → counted as failure. (`handlers_feed.go:146-168`) |
| **J5 reaction toggle** | `POST /posts/{id}/vote`, form `dir=up`, header `HX-Request: true` | **200** (vote-control fragment) | toggle semantics (second `up` removes) — equivalent to A's idempotent add/remove pair. Without `HX-Request` → 303. (`handlers_feed.go:417-441`) |
| **J4 thread + photo** (in mix v2, 3 %) | `POST /uploads` multipart field `file` (a 256×256 RGBA PNG generated per journey — random pixels, so B's SHA-256 blob de-duplication never short-circuits the store) → **202** `{"upload_id","status":"processing"}`; `GET /uploads/{upload_id}/status` every **1 s** (the browser's cadence, `upload.ts`) until `{"status":"completed","result":{"id","url",…}}` (`failed` → journey failure); then `POST /posts`, form `community=<postableSlug(u)>`, `title`, `body` with `![](/uploads/<id>)` embedded (what the composer's `data-md-insert` does — no `attachments` field) | 202 / 200… / **303** `Location: /thread/{newId}` | membership required (§4.2; the seed's `off=0` space). Rejected → 303 `/` → failure. 401 JSON without session; 413/415/422 on bad files (`handlers_media.go:53-230`). The journey time includes the poll waits by design (deviation D12). A's equivalent: initiate → presigned PUT → commit → create thread with `![photo](image:<id>)` → attach (5 calls, no wait). |
| **J9 DM** (in mix v2, 4 %) | `POST /messages/{peerUserId}`, form `body` | **303** `/messages/{peerUserId}` — **also on rejection** (non-friend, empty body) | friends only; peers scraped per session from `GET /messages` (`href="/messages/<id>"`, the seed's ring ±1/±7/±40 — account ids are not derivable from usernames). The non-diagnostic 303 is deviation D13: the write is verified through the DM-table delta recorded per run. A's equivalent: `POST /api/social/conversations/direct` once per VU, then `POST /api/social/conversations/{id}/messages` (200 + id). |

### 4.6 WebSocket (J8 hold)
- `GET /ws` (`server.go:241`), cookie `forum_session` at upgrade; anonymous sockets are accepted and
  receive broadcasts (`live.go:19-35`). Origin check = same rule as CSRF; missing Origin allowed.
- **No subscribe protocol**: every socket receives every public post/comment broadcast as JSON
  `{"type":"post|comment|…","postId","parentId","community","html","htmlByLocale":{…}}`
  (`live.go:159-176`, `wstypes.go:11-27`); the client filters by community/post. The harness holds
  the socket 120 s, counts messages with `type` ∈ {`post`,`comment`}, reconnects.
- No ping/keepalive, no server idle timeout (`hub/hub.go:149-205`); ingress `proxy-read-timeout 3600`.
- Semantics differ from A (A subscribes per view and is authorized per push). J8 compares **hold
  cost and receipt**, not fan-out semantics — documented.
- **Receipt probes (harness h4):** every 30 s a holding VU posts one comment (`POST /posts/{id}/comments`,
  tagged `probe_comment`) and times the request's start until the frame whose `html` contains
  `id="comment-<thatId>"` (the id from the 303's `#comment-N`) arrives on its own socket →
  `forum_ws_receipt`; unanswered after 15 s → `forum_ws_probes_lost`. B publishes the frame to Redis
  **before** writing the 303 (`handlers_feed.go` → `broadcastComment` → `publishBus`), so a frame can
  reach the VU before the response is parsed — the adapter keeps such early frames until the id is known.
  Smoke 2026-10-18: B median ≈ 5 ms vs A ≈ 1.5 s (A's outbox relay polls every 5 s — deviation D14).
- The hub closes a socket whose 32-frame send buffer is full (`client too slow to keep up`, close 1008,
  `hub.go:156-162`) — the source of B's WS errors under stress/capacity in round v1; the probes'
  loss counter now shows what such a close costs a reader.

### 4.7 Status/failure conventions used by the harness
- "Success" per journey: J1/J2/J6/J7/nav → 200; J3/J4/J9 → 303 with the expected `Location`;
  J5 → 200. Anything else counts toward `http_req_failed`. **Any 429 anywhere fails the run** (both
  sides) — hence `GOMX_AUTH_RATELIMIT=0` must be live and is recorded in `meta.json`.

---

## 5. Definition of done for tomorrow (2026-10-17) — minimum set

1. Branch `ab-benchmark` from `master` with **B-01, B-02, B-03, B-04, B-05, B-05a** committed and pushed.
2. `task lint` and `task test:go` green on the branch.
3. `kubectl kustomize deploy/k8s/bench` renders (no HPA, image `gomx:bench`, `GOMX_DB_MAX_CONNS=30`).
4. Tell Jakub the branch/commit; he builds the image on his machine (`docker build -f ContainerFile.dev`)
   and deploys into his cluster. If anything in B-05 blocks you, ship B-01…B-04 and he will author
   the overlay from this document (it is complete above) — but then any rendering fix lands in his
   checkout of your repo, which you should merge back.

**Self-check without Jakub's cluster** (your machine, Postgres via podman):
```bash
task db:reset && task db:up
sleep 5
GOMX_SEED=bench GOMX_AUTH_RATELIMIT=0 GOMX_COMPRESS=0 GOMX_NOTIFY_FRIENDS_ON_POST=0 \
  GOMX_DB_MAX_CONNS=30 DATABASE_URL='postgres://gomx:gomx@localhost:5432/gomx' PORT=8091 \
  go run ./cmd/server/main.go &
# wait for: benchmark seed complete  users=800 … posts=1600 …
curl -s http://localhost:8091/stats                                   # {"posts":1600,"comments":9000,…}
curl -s http://localhost:8091/c/general | grep -c 'id="post-'         # 20   (B-01)
curl -s http://localhost:8091/c/general | grep -o 'id="feed-sentinel"[^>]*hx-get="[^"]*"'   # has &amp;cursor=
curl -si -c /tmp/j -d 'username=benchuser0034&password=DemoPassword1' http://localhost:8091/login | head -5   # 303 + Set-Cookie forum_session
curl -si -b /tmp/j -d 'dir=up' -H 'HX-Request: true' http://localhost:8091/posts/5/vote | head -1          # 200
curl -si -b /tmp/j -d 'body=bench+comment' http://localhost:8091/posts/5/comments | grep -i '^location'    # /thread/5#comment-N
psql postgres://gomx:gomx@localhost:5432/gomx -c 'select count(*) from notifications'                      # 0 after a POST /posts (B-02)
curl -si http://localhost:8091/auth/google | head -3                                                       # 303 /login, no forum_session (B-03)
curl -sI -H 'Accept-Encoding: gzip' http://localhost:8091/ | grep -ci content-encoding                     # 0 (B-04)
```
Optionally run the shared harness yourself: `git clone https://github.com/JakubPatkowski/Forum-Master-Thesis-.NET`
then `k6 run -e TARGET=b -e PROFILE=smoke -e BASE_URL=http://localhost:8091 load/k6/main.js` (k6 only, no npm).

---

## 6. Deviations register — what stays different and how the paper describes it

| # | Difference | A | B | Paper treatment |
|---|---|---|---|---|
| D-1 | Rendering | JSON API + CSR SPA (thread open = 3 calls: detail, tree, reaction batch) | one HTML document per interaction (templ + goldmark + bluemonday **per read**, nothing cached) | the thesis subject (H1/H2) |
| D-2 | Session | JWT validated in-process; no DB on reads | cookie → 1 SQL per authenticated request + 3 shell queries per full page (`session.go:88-102`); per-card `UserVote` lookups for signed-in viewers | paradigm; measured journeys are anonymous except J3/J5 |
| D-3 | IDs / paging | ULID, keyset everywhere | int64 identity; feed cursor (keyset), search OFFSET | disclosed, not normalized (§3.7) |
| D-4 | Search | `tsvector` + `websearch_to_tsquery`, keyset with `ts_rank` | `ILIKE '%q%'` + pg_trgm GIN, OFFSET | paradigm; both currently match ~all rows (see B-09 note) |
| D-5 | Messaging backbone | outbox → RabbitMQ (≈150–250 Mi RSS pod) | Redis pub/sub (≈ 10 Mi) | H1 namespace totals include them deliberately; qualitative section |
| D-6 | Push on reaction | yes (outbox → relay → hub) | none | not measured; note under J5 that A does more work per like |
| D-7 | Live-push semantics | per-view subscription, authorized per push | broadcast to every socket, per-locale double render | J8 = hold cost + receipt only |
| D-8 | Comment delete | tombstone | cascade | OQ-1, not measured |
| D-9 | Uploads | presigned PUT bypasses app | server ingest, async River, thumbnails on read | paradigm; J4 image arm P1 |
| D-10 | Login KDF | Argon2id (~400 ms) | PBKDF2-SHA256 100k | excluded from hot-path percentiles (setup only) |
| D-11 | Logging | one Info JSON line per request; probes Verbose | one Info JSON line per request incl. `/healthz`, `/static/*` | negligible; k6 fetches no static assets |
| D-12 | Security headers | CSP `default-src 'self'`, nosniff, frame DENY, no HSTS | full CSP, HSTS when secure cookies, Permissions-Policy | not measured |
| D-13 | Processor visibility | .NET `ProcessorCount` = 1 under a 1-CPU limit (`DOTNET_ThreadPool_MinThreads=64` set) | Go 1.26 container-aware `GOMAXPROCS` = 1 under the same limit | symmetric; recorded |
| D-14 | Background work in app pods | outbox relay + consumer + orphan sweep + WS hub | River workers (shared pgx pool), orphan sweep, Redis bus | symmetric in kind |
| D-15 | A's frontend pod | separate `frontend` Deployment (1 × 50m/128Mi → 300m/256Mi) in A's namespace | rendering is inside the app pod | H1 counts whole namespaces (§I13) — A's SSR counterpart cost is the frontend pod |
| D-16 | Dataset shape until B-09 | reference | §B-06 list | recorded per run in `meta.json` |
| D-17 | Private-space read gating | **none on A's Content read endpoints** (a real authz gap found in this audit; A fixes it P1) | `CanViewCommunity` + membership lookup on private pages | measured journeys are public-only; disclosed |
| D-18 | Lighthouse lab setup | SPA served by `next start :3000` against a local API on `:5099` → every data call pays a CORS preflight the cluster does not (same origin there) | single binary, in-memory demo seed | both audits are demo-scale and local; the A preflight cost is disclosed next to the Lighthouse table |

---

## 7. Open decisions for Jakub (your input welcome)

1. **Fixed 2 replicas vs. HPA on Day 0** — recommendation: fixed 2 on both; HPA 1–3 @ 70 % (A's
   behaviour block) in a later class on both.
2. **TLS for B** (gomx.local SAN) vs. documenting the asymmetry — recommendation: TLS (cost ≈ 20 min
   on A, one overlay file on B).
3. **Tracing off on both** vs. 100 % on both — recommendation: off for measured runs.
4. **Day-0 mix without J4/upload** (renormalized J1 33 / J2 22 / J6 11 / nav 8 / J3 11 / J5 13 / J7 2)
   vs. keeping A's J4 unmatched — recommendation: drop J4 from both until the B adapter has it.
5. **Who moves on D2–D6** — recommendation: B adopts A's generators (B-09); A adopts B's social
   volumes (2 400 / 80 / 2 000) for J9.
6. **Word bank size** — both sides move to a selective (~300-word, Zipf-weighted) bank so J6 stops
   being a sort-everything query; needs one shared data file. Decide before the stress runs.

## 8. Explicitly NOT asked of you
No ACL/RBAC engine, no soft-delete/audit columns, no outbox/broker, no ULID keys, no groups, no
blocks, no tags, no global feed changes, no removal of voice/video/OAuth/i18n/SEO/templbook/Android/
GoF code, no change to your k6 script (it stays yours for your own dashboards), no change to your
monitoring stack, no second seeding mechanism, no new rate-limiter semantics. Your CI already tests
on `postgres:18-alpine` (`.gitlab-ci.yml:197`) — the earlier "16→18" ask is closed.
