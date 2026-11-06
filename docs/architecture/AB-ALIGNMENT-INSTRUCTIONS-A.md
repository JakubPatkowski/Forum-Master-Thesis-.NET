# A/B Alignment — Implementation Instructions for Architecture A (forum-dotnet)

**Audience:** Jakub Patkowski and the Claude sessions implementing the A side. Companion of
`AB-ALIGNMENT-INSTRUCTIONS-B.md` (sent to Hubert). §4 (interface contract), §6 (deviations) and
§7 (open decisions) are **byte-identical** in both documents — edit them in one and copy to the other.

| | |
|---|---|
| **Purpose** | Get real, honest preliminary A-vs-B numbers for the class on 2026-10-17 at the lowest cost, and lay out the P1/P2 work for the four following classes. |
| **Snapshot — A** | `forum-dotnet` branch `29-feat-phase-12---tauri-desktop-application` @ `ee0faee` ("feat: Tauri core") + staged, uncommitted A-2…A-11 work (k6 journey tagging, PG 18, Lighthouse, trivy, RUM, …). Cluster `forum` is **Stopped**; its Postgres PVC still holds **PG 17** data (the statefulset now says `postgres:18` → a wipe is required, see runbook step 1). `.env`: 8 CPU / 10 240 MiB (not the 6 CPU of `.env.example`). |
| **Snapshot — B** | `gomx` branch `master` @ `8b23bc1` (2026-10-15). Hubert implements the B document on branch `ab-benchmark` tonight. |
| **How to use** | §3 is ordered P0 → P1 → P2; each item is implementation-ready (files, before/after, tests, verification). §5 is the Day-0 runbook — run it top to bottom tomorrow. If B is not deployable, §5.9 is the fallback that still yields real numbers. |
| **Hard rules carried over** | No git commits/pushes by Claude (owner-only). Central Package Management only. English in repo files. |
| **Priority legend** | **P0** = tomorrow · **P1** = next classes · **P2** = optional. |

---

## 1. Verification summary of the plan (what was CONFIRMED / WRONG / UNCERTAIN)

Each ID refers to the plan reviewed on 2026-10-16. Evidence is `path:line` in the respective repo
(A = `forum-dotnet`, B = `gomx`).

| ID | Verdict | Evidence / correction |
|---|---|---|
| I1 | **CONFIRMED** (+ nuance) | B manifests are PSS-`restricted`-compliant, no hostPath/NodePort/storageClass; kustomize base at `B deploy/k8s/kustomization.yaml:9-30`; no `bench` overlay exists. B's `task k8s:*` assumes rootless podman (`Taskfile.yml:422-429`); on the docker-driver cluster use `docker build` + minikube docker-env (same as A's `deploy.sh:105`). B's netpol admits ns `ingress-nginx` and `monitoring` (`networkpolicy.yaml:36-47`) — matches A's cluster. **Do not install B's monitoring stack** (`helm … kps … -n monitoring`, `Taskfile.yml:447`) — it would collide with A's `monitoring` release in the same namespace. |
| I2 | **WRONG in one detail** | `ContainerFile.dev` final stage is `FROM scratch` (`:65`), binary + `static/` copied (`:69-72`), **no `USER`** → pod `runAsUser: 65534` supplies it (`app.yaml:102`). A does **not** use `minikube image load`: images are built inside minikube's docker daemon via `eval $(mk docker-env)` (`A scripts/deploy.sh:105`) with `imagePullPolicy: Never`; B uses `IfNotPresent` (`app.yaml:97`) — same mechanism works. |
| I3 | **CONFIRMED** | A `k8s/backend/deployment.yaml:49-50` 150m/256Mi → 750m/512Mi; B `app.yaml:51,162-168` replicas 3, 100m/128Mi → 1/512Mi. A frontend 50m/128Mi → 300m/256Mi ×1 (`k8s/frontend/deployment.yaml:11,27-28`). |
| I4 | **CONFIRMED** | A `hpa.yaml:11-21` 1–3 @ 70 %, up 1 pod/30 s (30 s window), down 1/60 s (180 s); B `hpa.yaml:16-37` 2–10 @ 60 %, up 2 pods/30 s (0 s window), down 1/60 s (120 s). |
| I5 | **CONFIRMED** | A `k8s/postgres/statefulset.yaml:28,31,48-49` `postgres:18`, `shared_buffers=256MB effective_cache_size=512MB max_connections=100`, 250m/512Mi → 1000m/1Gi; B `postgres.yaml:49,86-92` `postgres:18-alpine`, **no args**, 100m/128Mi → 1/512Mi. |
| I6 | **CONFIRMED, default pinned** | B builds the pool with `pgxpool.ParseConfig(dsn)` + `NewWithConfig` (`postgres.go:40,52`), no MaxConns in code → pgx v5.11.0 default `max(4, runtime.NumCPU())` (`pgxpool/pool.go:19,384-387`, fetched from the tag) = **8 on this 8-CPU node**. `pool_max_conns=30` in the URL is honoured by the pool **but crashes startup** — `runMigrations` passes the raw DSN to `database/sql` (`postgres.go:92-95`), verified live 2026-10-17 (`FATAL: unrecognized configuration parameter "pool_max_conns"`). Hence B sets `GOMX_DB_MAX_CONNS` in code (B-05a). River shares that pool (`forum.go:75`). A: `Maximum Pool Size=30;Minimum Pool Size=2` lives in the `postgres-credentials` secret (`deploy.sh:62`). |
| I7 | **CONFIRMED** | B `router.go:56` `Compress(5)` unconditional; A gzips via the ingress-nginx controller ConfigMap (`setup-minikube.sh:43-47`) — **cluster-global**, so B's ingress is compressed by it too. |
| I8 | **CONFIRMED** | B ingress plain HTTP (`ingress.yaml`), `GOMX_SECURE_COOKIES=false` (`app.yaml:130`); flag adds `Secure` + HSTS (`session.go:147-154`, `security.go:59-66`). A: `mkcert-tls.sh:21` SANs `forum.local, minio.forum.local, grafana.forum.local`; secret `forum-tls` in ns `forum-dotnet` only. |
| I9 | **CONFIRMED, one A nuance** | B: endpoint unset → no exporter at all (`tracing.go:47-50,136-141`); default sample ratio **1.0** (`:150-158`); logs JSON Info 1 line/request (`logging.go:95-118`). A: OTLP exporter is **always registered** (`ObservabilityExtensions.cs:38-44`); empty `Otlp:Endpoint` falls back to the SDK default `localhost:4317` → silent export failures, not "off"; **no sampler** = 100 %. Hence A-07. |
| I10 | **WRONG premise, otherwise CONFIRMED** | `GOMX_AUTH_RATELIMIT=0` disables (`server.go:260-262`); `GOMX_BEACON_RATELIMIT` likewise. **No write throttle exists**: `forum_writes_rejected_total` counts domain rejections (blank body / not a community member — `metrics.go:101-110`, `handlers_feed.go:141,166`), `WriteRejectionsHigh` is a funnel alert. Important consequence for the harness: **posting requires community membership** (`rules.go:229-232`) — see §4.2. |
| I11 | **CONFIRMED** | `run-load-test.sh:53-68` hard-codes `kc` (ns `forum-dotnet`), `hpa backend`, `-l app=backend`; no `TARGET` env is passed to k6 (`:84-89`). |
| I12 | **CONFIRMED, cheaper than P1** | A's Prometheus has `serviceMonitorSelectorNilUsesHelmValues: false` and **no namespace selector** (`values-kube-prometheus-stack.yaml:25-27`) → B's `deploy/monitoring/servicemonitor.yaml` works as-is once applied. P0. |
| I13 | **CONFIRMED** | pod sets documented in §5.6. |
| I14 | **CONFIRMED + two new findings** | B seeds in-process at first boot when `accounts` is empty (`forum.go:112-114,128-140`); migrations hold `pg_advisory_lock(7462839174)` (`postgres.go:99-105`) but the IsEmpty→seed window has **no lock** → multi-replica first boot double-seeds and crash-loops. **New:** the HTTP listener starts *after* seeding (`cmd/server/main.go:44` NewRouter→buildForum seeds, `:60` listens), so the base liveness probe (5 s + 3×10 s) kills a seeding pod → restart sees `IsEmpty=false` → **partial dataset**. The B overlay adds a `startupProbe`. |
| D1 | **CONFIRMED** | A `SeedPlan.cs:112-117` 800/12(4 private ×25)/1600/9000/15000; B `seedbench.go:35-45` 800/12/1600/9000/12000+3000 (+80 pending, 2000 DMs). |
| D2 | **CONFIRMED, member count WRONG** | B has 2 private (`bench11`,`bench12`, `seedbench.go:141-143`) with **~200 members each** (membership formula `(u, u+5, u+9) % 12`, `:155-170`), not a small group. |
| D3 | **CONFIRMED** | A `index % 100 == 0` (16); B post 1 only (`:196-200`). |
| D4 | **CONFIRMED, "Zipf" WRONG** | A is **squared-uniform** (`SeedDistribution.cs:17-18` `count * u²`), not Zipf; B 25 % on 16 hot posts (`:212-231`). |
| D5 | **CONFIRMED + new** | B 12000/3000 and **1 in 10 post votes is a downvote** (`:270-284`); A 75/25 likes only. |
| D6 | **CONFIRMED + new** | A: 31 unique words (`SeedText.cs:12-18`, "keyset" twice), 120–320 words/body (`ContentSeeder.cs:120`); B: 10 sentences (`seedbench.go:380-391`). k6 `SEARCH_WORDS` is **not** byte-identical to the bank (drops the duplicate, adds `seeded`) — harmless. **New:** with a 31-word bank every word is in nearly every body → J6 on both sides is a match-everything query (OD-6). |
| D7 | **CONFIRMED** | B 2400 accepted (ring + chords 1/7/40, `:306-326`), 80 pending, 2000 DMs; A `SocialSeeder.cs:68-71` no-ops in Benchmark. |
| D8 | **CONFIRMED** | A 20 blocked + ~1 % deleted threads/comments (`SeedPlan.cs:112`, `ContentSeeder.cs:116,181`); B none (hard delete only). |
| D9 | **CONFIRMED** | A `bench_user_0001..0800@bench.local` / `Bench#Password1` (login by **email**); B `benchuser0001..0800` / `DemoPassword1` (login by **username**). |
| Z1 | **CONFIRMED; test claim WRONG** | `handlers_feed.go:16` = 8, `handlers_search.go:20` = 10; **no Go or Playwright test pins either** — nothing to update. |
| Z2 | **CONFIRMED** | `notifications.go:58-70`, called from `live.go:234`; per friend: INSERT + unread COUNT + 2 renders + bus publish; no flag today. |
| Z3 | **CONFIRMED** | `GetThreadFeedEndpoint.cs:26-29` → 422 `feed.category_required`; only a per-category index `ix_threads_feed` exists (`InitialContent.cs:209-214`). Search route is `GET /api/content/search` (`SearchThreadsEndpoint.cs:16`), not `/threads/search`. |
| Z4 | **CONFIRMED** | A pushes `ReactionAdded/Removed` (`RealtimeEventMap.cs:24-45`); B vote/like handlers broadcast nothing (`handlers_feed.go:417-461`). |
| Z5 | **CONFIRMED** | A tombstone `Comment.cs:18`; B `DELETE FROM comments` + `ON DELETE CASCADE` (`queries.sql:368-369`, `00012_nested_comments.sql:2`). |
| Z6 | **CONFIRMED** | both load the whole tree in one unbounded query (`ContentQueries.cs:186-193`; `queries.sql:140-152`). B signed-in viewers add a `CommentLikeExists` per comment (`handlers.go:70-83`) — anonymous J2 avoids it. |
| Z7 | **CONFIRMED** | 202 `{"upload_id","status":"processing"}` on the Postgres+River+S3 path (`handlers_media.go:181-184`); status endpoint `:187-230`. |
| Z8 | **CONFIRMED** | `http.go:98-105` renders EN+PL on every broadcast. |
| Z9 | **CONFIRMED** | `handlers_oauth.go:131-137,145-151,159-164`; no env gate; no tests. |
| K1 | **CONFIRMED ×3** | `forum.ts:46-78` one shared cookie; `:157` `GET /feed?page=2` while the handler selects pages only by `cursor` (`handlers_feed.go:74-77`; the `offset` arg of `feedViews` is unused); `:207-208` expects 201, server returns 202. |
| K2–K13 | **CONFIRMED with corrections** | contract in §4: login by `username`; success **303** (422 on failure); CSRF = Origin-vs-Host, missing Origin allowed (`csrf.go:27-36,54-64`); profile `/u/{numericId}`; comment create always 303 to `/thread/{id}#comment-{newId}`; vote 200 fragment only with `HX-Request: true`; WS `/ws` has **no subscribe protocol**, anonymous allowed. |
| M1 | **CONFIRMED, no Makefile target** | `scripts/lighthouse.sh` builds + serves the SPA locally (`next start :3000`) against a local Development-seeded API at `:5099` (`:29-31,94,101`) — cross-origin, so the audit pays CORS preflights the cluster doesn't (§6 D-18). B `lighthouserc.json` audits `/` and `/thread/1` of an in-memory demo-seeded binary (`scripts/lighthouse.sh:49`). Same runs (3), same budgets, both default throttling. |
| M2 | **CONFIRMED, no Makefile target** | `scripts/artifact-size.sh` (needs `npm run build` + images); B `ci-artifact-metrics.sh` needs `bin/server` (no Go toolchain on this box → extract from the image, §5.7). |
| M3 | **CONFIRMED** | `cloc` is not installed; `npx -y cloc` works. Exclusion lists in §5.7. |
| M4 | **CONFIRMED, version not pinned** | `security-scan.sh:40-48` byte-identical flags to B's CI; trivy 0.72.0 in `~/.local/bin`; the version is recorded from the JSON, not pinned by the script. |
| M5/M6/M7 | **CONFIRMED** | scripts exist (`sonar-scan.sh`, `dev-loop-bench.sh`); desktop row needs Windows. |
| R1 | **CONFIRMED** | `bench-run.sh:73,87-122,228-246` layout + `meta.json` + `stats.json`; only `thesis/results/A/` exists. |
| R3 | **WRONG** | no B tests exist for Z1/Z9; Z2 has `TestNotifyFriendsOfPost` (`notifications_test.go:50`) which keeps passing (default on) — B adds a disabled-path test. |
| plan "B-3 CI PG 16→18" | **STALE** | B CI already uses `postgres:18-alpine` (`.gitlab-ci.yml:197`). |

**Found by the hunt (not in the plan):** (1) **A's Content read endpoints perform no private-category
check** (`grep Visibility.Private` hits only `CreateThread.cs:87`, `CategoryAccessReader.cs:22`) —
a real authz gap, also a fairness asymmetry vs. B's `CanViewCommunity` → A-12. (2) B probes lack
`timeoutSeconds` (1 s default) — the exact bug A fixed in 9c. (3) B pays a session SQL per
authenticated request + 3 shell queries per full page (`session.go:73-102`) — paradigm, documented.
(4) B's seed-vs-liveness kill (I14). (5) Both sides run 100 % tracing in-cluster. (6) `GOMAXPROCS`
(Go 1.26, container-aware) and .NET `ProcessorCount` both resolve to 1 under a 1-CPU limit —
symmetric. (7) RabbitMQ (A) vs Redis (B) footprint lands in the H1 namespace totals by design.
(8) HTTP/2: both ingresses will negotiate h2 with k6 once B is on TLS; ingress→upstream is
HTTP/1.1 on both. (9) Both monitoring stacks do **not** fit together and are not needed together —
only A's is installed (sum of its requests 610m / 1680Mi).

---

## 2. Decision summary — the golden mean

| Feature / aspect | A | B | Decision | Reason |
|---|---|---|---|---|
| Spaces, threads, comments (depth 5), like/upvote, pinned float, search, profile, friends, 1:1 DM, friend-request notification, live push of new thread/comment | yes | yes | **Core** — measured journeys use only this | shared surface |
| Global ("home") feed | missing (SPA merges per-category feeds client-side) | yes (`GET /`) | J1 = **single-space feed on both** for now; A adds a global feed later (A-10, P1) | cheapest honest J1 today |
| Page size | 20 | 8 / 10 | **B → 20** | F1 |
| Friend fan-out on new post | none | durable notification + push per friend | **B: env flag, off in bench**; feature stays | F1; outside spec core |
| Push on reaction | yes (WS) | no | document; **do not add** to B | not measured |
| Comment delete | tombstone | hard delete + cascade | document (OQ-1) | not measured |
| Upload | presigned PUT + commit | multipart → 202 + River + status poll | document; harness polls (P1) | paradigm |
| WS fragments rendered per locale | n/a | 2× per broadcast | leave, document | J8 is a hold |
| OAuth mock login | n/a | yes when creds unset | **B gates behind `GOMX_OAUTH_MOCK=1`** | security |
| Compression | ingress | in-app | **B: `GOMX_COMPRESS=0` in bench**; ingress gzips both | F7 |
| TLS | `https://forum.local`, Secure cookies | plain http | **B behind TLS too** (A-04 adds `gomx.local` SAN + `gomx-tls` secret; B overlay has the TLS block) | equal transport + cookie posture |
| Replicas / HPA | HPA 1–3 @ 70 % | HPA 2–10 @ 60 %, replicas 3 | **Day 0: fixed 2 replicas, no HPA, both.** Later: HPA 1–3 @ 70 % + A's behaviour block on both | autoscaler noise out of the first numbers |
| App resources | 150m/256Mi → 750m/512Mi | 100m/128Mi → 1/512Mi | **both → requests 250m/256Mi, limits 1000m/512Mi** (A-05) | F3 |
| Postgres | PG 18 tuned, 250m/512Mi → 1000m/1Gi | PG 18 defaults, 100m/128Mi → 1/512Mi | **B copies A's args + limits** | F3 |
| DB pool | 30 | default 8 | **B: `GOMX_DB_MAX_CONNS=30`** (B-05a: env + 6 lines in `postgres.New`; a `pool_max_conns` URL param crashes B — goose opens the raw DSN via `database/sql`) | F3 |
| Rate limiters | raised to 1e6 for runs, recorded | auth 10/min/IP | **B: `GOMX_AUTH_RATELIMIT=0`**, recorded | F5 |
| Tracing | 100 % → Tempo | 100 % → Tempo | **OFF on both during measured runs** (A-07) | one variable fewer |
| Dataset distribution (D2–D6) | reference | differs | **record for Day 0; B adopts A's generators (P1)**; A exports nothing new | B's seed is small and index-driven |
| Social seed | 0 rows in Benchmark | 2400 / 80 / 2000 | **A adopts B's numbers** (A-11, P1) | J9 |
| Downvotes | none | 10 % of votes | **B → 0 % in bench seed** (P1) | parity |
| Mix (Day 0) | 9c weights incl. J4 5 % + upload 3 % + tags 8 % | — | **v0-day0: J1 33 / J2 22 / J6 11 / nav(list spaces) 8 / J3 11 / J5 13 / J7 2**, J4+upload weight 0 until the B adapter has them (A-01) | every measured journey exists on both sides |
| Features outside core (both lists) | keep | keep | **keep all; describe qualitatively**; generated code excluded from core-scope static metrics | no measured-path impact |
| Physical cuts | none | none | **none** | flags cover every measured-path impact found |

---

## 3. Work items

### P0 — tomorrow

> **Status 2026-10-18 (night) — independent re-verification.** Content migration `AddOwnerActivityIndexes`
> (profile reads were seq scans; B had author indexes), `lib-bench.sh` B reseed → fresh pods, idle-window /
> marginal CPU per journey in `ab_window.py`/`ab-round.sh`/`ab_report.py`; D9–D11 recorded. See `AB-START-HERE.md` §4.2/§5.0.
>
> **Status 2026-10-17:** A-01, A-02, A-03, A-04, A-05, A-07, A-08 implemented and locally verified (k6 smoke on both targets through the runner, 0 % failed; 53/53 Api unit tests; scripts `bash -n`; overlay renders). A-06 and A-09 are cluster-time steps (runbook §5). Found while testing: k6 v2.1.0 `jar: null` does not disable the VU cookie jar (adapter uses a fresh `http.CookieJar()` per request); `pool_max_conns` in B's `DATABASE_URL` crashes B's goose step (→ `GOMX_DB_MAX_CONNS`, B-05a).

#### A-01 — Shared k6 harness: `TARGET=a|b` adapters, Day-0 mix, J1 batch parity

- **Invariant:** F4 (one harness, same profiles/mix/think time/thresholds/summary on both sides) and
  F1 (J1 on A today omits the reaction batch the real SPA issues; the mix contains A-only tag
  autocomplete, J4-with-tags and presigned upload that B cannot run yet).
- **Current code:** `load/k6/main.js` (507 lines, staged): A-specific setup `:179-252`, mix
  `:294-386` (J1 `:300-309` — `threadFeed` only, no batch; `nav` = tag suggest `:334-336`; J4
  `:345-349`; upload `:365-374`), `wsHold` `:392-426`, `hosts` `:169-171`, `handleSummary`
  `:434-507`. Helpers `load/k6/lib/api.js` (A REST), `lib/assets.js`. No `TARGET`, no `targets/`.
- **Design:** keep `main.js` as the only entrypoint (profiles, thresholds, mix, journey metrics,
  summary — all shared), move everything that touches a wire format into a **target module** with
  one interface. `main.js` does `const target = TARGET === 'b' ? b : a;`.

  Target interface (`load/k6/targets/README.md`):
  ```js
  export const ENDPOINTS;                 // tag values for per-endpoint informational thresholds
  export function hosts(ingressIp);       // k6 `hosts` pin map
  export function setup(baseUrl, poolSize, loginStaggerMs);
  //   → { sessions: [{ auth, userIndex }], spaces: [{ key, slug }], threadsBySpace: { key: [{ id, ownerId }] } }
  export const journeys = { J1, J2, J3, J5, J6, J7, nav };   // (baseUrl, data, session, pick) → void; use track()
  export function wsHold(baseUrl, data, session, holdMs, counters);
  ```
  `track(res, name, okStatuses)` moves to `lib/track.js` (same semantics: 429 → `forum_rate_limited`,
  check on allowed statuses).

  **`load/k6/targets/a.js`** = today's A code lifted out of `main.js`, with two changes:
  ```js
  // J1 — the SPA's real feed render = page + reaction batch over the 20 card ids (G22 parity).
  J1(baseUrl, data, session, pick) {
    const space = pick.space(data);
    const first = threadFeed(baseUrl, space.key, null, 20);
    if (!track(first, 'feed')) return;
    const body = first.json();
    const ids = body.items.map((t) => t.id);
    if (ids.length) track(reactionBatch(baseUrl, 'thread', ids), 'reactions_batch');
    if (Math.random() < 0.3 && body.hasMore && body.nextCursor) {
      const second = threadFeed(baseUrl, space.key, body.nextCursor, 20);
      if (track(second, 'feed')) {
        const ids2 = second.json().items.map((t) => t.id);
        if (ids2.length) track(reactionBatch(baseUrl, 'thread', ids2), 'reactions_batch');
      }
    }
  },
  // nav — list spaces (replaces A-only tag autocomplete so both sides have the same navigation call)
  nav(baseUrl) { track(listCategories(baseUrl), 'categories'); },
  ```
  `space.key` = category ULID on A, slug on B. J4 / upload code stays in `a.js` (weight 0 in mix v0).

  **`load/k6/targets/b.js`** (new — complete skeleton, matches §4):
  ```js
  import http from 'k6/http';
  import { sleep } from 'k6';
  import ws from 'k6/ws';
  import { track } from '../lib/track.js';

  export const ENDPOINTS = ['login', 'communities', 'space_page', 'space_feed', 'thread', 'search', 'profile',
    'comment_create', 'vote', 'ws'];
  export function hosts(ip) { return ip ? { 'gomx.local': ip } : {}; }

  // Bench seed: benchuser0001..0800 / DemoPassword1 (seedbench.go:99-101). Same ordinary band as A (0034+).
  const USER_BASE = 33, PASSWORD = 'DemoPassword1';
  // Community index → slug (seedbench.go:124-134); 10,11 are private. User u (0-based) is a member of
  // u%12, (u+5)%12, (u+9)%12 (seedbench.go:155-170) and may POST only there (rules.go:229-232).
  const SLUGS = ['general', 'showcase', 'golang', 'design', 'help', 'random',
    'bench7', 'bench8', 'bench9', 'bench10', 'bench11', 'bench12'];
  export function postableSlug(userIndex) {
    for (const off of [0, 5, 9]) { const i = (userIndex + off) % 12; if (i < 10) return SLUGS[i]; }
    return 'general';
  }
  const noJar = () => ({ jar: new http.CookieJar() }); // fresh jar per request: k6 `jar: null` does NOT disable the VU jar
  const g = (base, path, endpoint, extra = {}) =>
    http.get(`${base}${path}`, { ...noJar(), redirects: 0, tags: { endpoint }, ...extra });
  const unesc = (s) => s.replace(/&amp;/g, '&');

  function login(base, username) {
    const res = http.post(`${base}/login`, { username, password: PASSWORD },
      { ...noJar(), redirects: 0, tags: { endpoint: 'login' } });
    if (res.status !== 303 || !res.cookies.forum_session) return { auth: null, status: res.status };
    return { auth: `forum_session=${res.cookies.forum_session[0].value}`, status: res.status };
  }
  export function setup(base, poolSize, staggerMs) {
    const probe = login(base, `benchuser${String(USER_BASE + 1).padStart(4, '0')}`);
    if (probe.status === 429) throw new Error('B auth limiter active — GOMX_AUTH_RATELIMIT=0 missing in the bench overlay');
    if (!probe.auth) throw new Error(`B login failed (${probe.status}) — is GOMX_SEED=bench applied and /stats posts=1600?`);
    const sessions = [{ auth: probe.auth, userIndex: USER_BASE }];
    for (let i = 1; i < poolSize; i++) {
      const r = login(base, `benchuser${String(USER_BASE + 1 + i).padStart(4, '0')}`);
      if (r.status === 429) throw new Error('B auth limiter hit mid-pool');
      if (r.auth) sessions.push({ auth: r.auth, userIndex: USER_BASE + i });
      sleep(staggerMs / 1000);
    }
    // Anonymous discovery: public communities from /communities (community.templ:52), threads + authors
    // from the first two feed pages of each (post.templ:29, pages.templ:198-207).
    const list = g(base, '/communities', 'communities');
    const spaces = [...new Set([...list.body.matchAll(/href="\/c\/([a-z0-9-]+)"/g)].map((m) => m[1]))]
      .filter((s) => s !== 'new').map((slug) => ({ key: slug, slug }));
    const threadsBySpace = {};
    for (const s of spaces) {
      const page = g(base, `/c/${s.slug}`, 'space_page');
      let html = page.body;
      const m = html.match(/id="feed-sentinel"[^>]*hx-get="([^"]+)"/);
      if (m) html += g(base, unesc(m[1]), 'space_feed').body;
      const seen = new Set(); const threads = [];
      for (const a of html.matchAll(/<article id="post-(\d+)"[\s\S]*?href="\/u\/(\d+)"/g)) {
        if (!seen.has(a[1])) { seen.add(a[1]); threads.push({ id: a[1], ownerId: a[2] }); }
      }
      threadsBySpace[s.key] = threads;
    }
    return { sessions, spaces, threadsBySpace };
  }
  export const journeys = {
    J1(base, data, _s, pick) {
      const sp = pick.space(data);
      const first = g(base, `/c/${sp.slug}`, 'space_page');
      if (track(first, 'space_page') && Math.random() < 0.3) {
        const m = first.body.match(/id="feed-sentinel"[^>]*hx-get="([^"]+)"/);
        if (m) track(g(base, unesc(m[1]), 'space_feed'), 'space_feed');   // /c/{slug}/feed?page=2&cursor=…
      }
    },
    J2(base, data, _s, pick) { track(g(base, `/thread/${pick.thread(data).thread.id}`, 'thread'), 'thread'); },
    J6(base, _d, _s, pick) { track(g(base, `/search?q=${encodeURIComponent(pick.word())}`, 'search'), 'search'); },
    J7(base, data, _s, pick) { track(g(base, `/u/${pick.thread(data).thread.ownerId}`, 'profile'), 'profile'); },
    nav(base) { track(g(base, '/communities', 'communities'), 'communities'); },
    J3(base, data, s, pick) {
      const t = pick.thread(data).thread;
      const res = http.post(`${base}/posts/${t.id}/comments`, { body: pick.commentBody() },
        { ...noJar(), redirects: 0, headers: { Cookie: s.auth }, tags: { endpoint: 'comment_create' } });
      track(res, 'comment_create', [303]);
      if (res.status === 303 && !/#comment-\d+$/.test(res.headers.Location || '')) track({ status: 599 }, 'comment_create', [303]);
    },
    J5(base, data, s, pick) {
      const t = pick.thread(data).thread;   // toggle: an even number of ups returns to the seeded state
      track(http.post(`${base}/posts/${t.id}/vote`, { dir: 'up' },
        { ...noJar(), redirects: 0, headers: { Cookie: s.auth, 'HX-Request': 'true' }, tags: { endpoint: 'vote' } }), 'vote');
    },
  };
  export function wsHold(base, _data, s, holdMs, c) {
    const res = ws.connect(`${base.replace(/^http/, 'ws')}/ws`, { headers: { Cookie: s.auth }, tags: { endpoint: 'ws' } }, (socket) => {
      socket.on('open', () => c.subscribed.add(1));          // B has no subscribe protocol: open == subscribed
      socket.on('message', (raw) => { const m = JSON.parse(raw); if (m.type === 'post' || m.type === 'comment') c.notifications.add(1); });
      socket.on('error', () => c.errors.add(1));
      socket.setTimeout(() => socket.close(), holdMs);
    });
    return res;
  }
  ```
  B search words until B-09: `['benchmark', 'rendering', 'fragment', 'comments', 'threads', 'trigram',
  'registration', 'private', 'timestamps', 'media', 'corpus', 'payload', 'replies', 'indexes', 'buckets']`
  (all from `seedbench.go:380-391`); `pick.word()` is target-provided.

  **`main.js` changes:** `const TARGET = (__ENV.TARGET || 'a').toLowerCase();` import both targets;
  `options.hosts = target.hosts(INGRESS_IP)`; `setup()` → `target.setup(BASE_URL, poolSize, LOGIN_STAGGER_MS)`;
  `traffic()` becomes a table walk:
  ```js
  export const MIX_VERSION = 'v0-day0';   // recorded in the journeys block; reweight = edit this table only
  const MIX = [['J1', 33], ['J2', 22], ['J6', 11], ['nav', 8], ['J3', 11], ['J5', 13], ['J7', 2]]; // Σ = 100
  export function traffic(data) {
    const session = data.sessions[(__VU - 1) % data.sessions.length];
    let roll = Math.random() * 100;
    for (const [name, weight] of MIX) {
      if (roll < weight) { runJourney(name, () => target.journeys[name](BASE_URL, data, session, pick)); break; }
      roll -= weight;
    }
    sleep(0.3 + Math.random() * 0.4);
  }
  ```
  `pick` = `{ space, thread, word, commentBody }` built in `main.js` from today's `pickCategory`/`pickThread`
  (generic over `data.spaces`/`data.threadsBySpace`) + `target.searchWords`. The journeys block gains
  `"target": TARGET, "mix_version": MIX_VERSION`. `wsHold` delegates to `target.wsHold` with the three
  counters; the demo/stress threshold `forum_ws_subscribed count>0` stays (B counts opens).
- **Tests/verification:**
  ```bash
  k6 inspect load/k6/main.js                                      # parses, both targets import
  # against local compose (A) — Development seed fallback must still work:
  k6 run -e TARGET=a -e PROFILE=smoke -e BASE_URL=http://localhost:5099 load/k6/main.js
  # against the cluster (after §5): smoke on both, 0 % failed, journeys J1/J2/J3/J5/J6/J7/nav all count>0
  scripts/run-load-test.sh smoke https://forum.local            # TARGET=a default
  TARGET=b scripts/run-load-test.sh smoke https://gomx.local
  ```
- **Acceptance:** both smokes pass thresholds; `journeys-*.json` for both carries the same seven
  journey keys and `mix_version: v0-day0`; no `nav`-tag or upload requests in either run.
- **Effort:** 3–5 h (the biggest P0 item). **Pitfalls:** k6 form POSTs must be plain objects (not
  JSON strings); keep `redirects: 0` on every B write (a followed 303 re-renders the whole thread
  and silently doubles J2 work); a FRESH `new http.CookieJar()` on every B request — **`jar: null` does not disable the VU jar on k6 v2.1.0** (verified live: the setup VU replayed the last login's `forum_session` on the "anonymous" discovery, exposing a private space, and the VU jar's `gomx_locale` cookie overrode the explicit `Cookie` header on writes → every J3/J5 303'd to `/login`); the sentinel regex depends on attribute order
  `id, class, hx-get` (`pages.templ:198-203`).

#### A-02 — `scripts/run-load-test.sh`: target-aware sampler and k6 env

- **Invariant:** F4/H1 — the 5-second sampler must record B's pods the same way it records A's.
- **Current code:** `:33-47` cluster detection via `kc get deployment backend`; `:53-68` sampler hard-coded
  to `hpa backend` and `-l app=backend` in `$K8S_NAMESPACE`; `:84-89` k6 invocation without `TARGET`.
- **Change:**
  ```bash
  TARGET="${TARGET:-a}"
  case "$TARGET" in
    a) BENCH_NS="$K8S_NAMESPACE"; BENCH_DEPLOY=backend; BENCH_HPA=backend; BENCH_LABEL="app=backend" ;;
    b) BENCH_NS="${GOMX_NAMESPACE:-gomx}"; BENCH_DEPLOY=gomx; BENCH_HPA=gomx; BENCH_LABEL="app=gomx" ;;
    *) die "TARGET must be a|b" ;;
  esac
  bk() { kubectl -n "$BENCH_NS" "$@"; }          # replaces kc in this script
  ```
  sampler: `bk get hpa "$BENCH_HPA" …` (empty when no HPA → nulls, already handled by the sed), `bk top pods -l "$BENCH_LABEL"`;
  field names stay (`backend_cpu_millicores` → keep the key for schema stability, document that it means
  "app pods"). The limiter warning block (`:39-46`) runs only for `TARGET=a`; for `b` warn if
  `bk get deployment gomx -o jsonpath='{…env[?(@.name=="GOMX_AUTH_RATELIMIT")].value}'` ≠ `0`.
  k6 call gains `-e TARGET="$TARGET"`; default `BASE_URL` = `https://forum.local` (a) / `https://gomx.local` (b).
  Output files gain the target: `summary-<target>-<stamp>.json` (bench-run's glob adapts).
- **Verification:** `TARGET=b scripts/run-load-test.sh smoke` produces `samples-b-*.json` with
  `pods_running: 2` and non-zero `backend_cpu_millicores` while `hpa_*` are null.
- **Effort:** 45 min.

#### A-03 — `scripts/bench-run.sh`: `TARGET=b` path (archive, meta, snapshots)

- **Invariant:** R1 — identical bundle layout for `thesis/results/B/`.
- **Current code:** preflight `:39-69` (backend deployment, monitoring svc, stale Job pods, seed sentinel
  via `psql` on `postgres-0` in A's schema), `meta.json` `:73-122`, limiter raise/restore `:124-147`,
  HPA grace `:160-164`, snapshots `:186-224` (A metric names), stats `:228-246`.
- **Change (minimal, branch on `$TARGET` — no second script):**
  - archive root `thesis/results/$(tr a-z A-Z <<<"$TARGET")/`;
  - preflight b: `kubectl -n gomx rollout status deploy/gomx`; seed sentinel = `curl -sk --resolve gomx.local:443:$INGRESS_IP https://gomx.local/stats` → `posts == 1600`; seed counts via
    `kubectl -n gomx exec postgres-0 -- psql -U gomx -d gomx -tAc "SELECT (SELECT count(*) FROM accounts)||' '||(SELECT count(*) FROM posts)||' '||(SELECT count(*) FROM comments)||' '||((SELECT count(*) FROM post_votes)+(SELECT count(*) FROM comment_likes))"`;
  - meta b: `architecture: "B (Go + Templ/HTMX SSR monolith)"`, `deployed_image`/`image_digest` from deploy `gomx`, `git_sha` = `git -C ../gomx rev-parse HEAD` (+dirty), `rate_limiter: { "GOMX_AUTH_RATELIMIT": "0" (from the live deployment env) }`, plus **both sides**: `replicas_fixed: 2`, `hpa: "absent (Day 0)"`, `tracing: "off"`, `resources: {requests, limits}` read from the live deployment, `seed_deviations: ["pinned 1 vs 16", …]` for b (static list from the B doc §B-06);
  - limiter raise/restore: skip for b (assert the env is `0`, else die);
  - HPA grace: skip when `bk get hpa "$BENCH_HPA"` returns nothing (both sides on Day 0);
  - snapshots b: `rps: sum(rate(http_requests_total{namespace="gomx"}[1m]))`,
    `p95_ms: histogram_quantile(0.95, sum by (le) (rate(http_request_duration_seconds_bucket{namespace="gomx", route!="/ws"}[1m])))*1000`,
    `p99_ms` likewise, `rps_by_route: sum by (route) (…)`, `errors_5xx: …{code=~"5.."}`, `backend_cpu_cores`/`backend_mem_bytes` with `namespace="gomx", pod=~"gomx-.*", container=""`, `pg_connections: sum(pg_stat_activity_count)` is A's exporter only → for b use `sum(pg_stat_activity_count{datname="gomx"})` if the exporter is pointed at it, else drop; `ws_connections: gomx_websocket_clients` (check the exact name in `internal/metrics/metrics.go`), drop the rabbit/outbox keys. Keep the same JSON keys so the thesis join is one script.
- **Verification:** `TARGET=b make bench ARGS="demo --repeats 1"` → `thesis/results/B/<stamp>-demo/{meta.json,warmup/,run-1/,prometheus-snapshots.json,stats.json}`; `stats.json` parses with the existing aggregator.
- **Effort:** 1.5–2 h. If time is short tomorrow: run `TARGET=b scripts/run-load-test.sh` ×3 by hand into
  `thesis/results/B/<stamp>-demo/run-N/` and write `meta.json` manually from the commands above — the
  layout matters more than the automation on Day 0.

#### A-04 — TLS for `gomx.local` (mkcert SAN + `gomx-tls` secret)

- **Invariant:** equal transport (TLS + h2 to the client) and equal cookie posture (`Secure`).
- **Current code:** `scripts/mkcert-tls.sh:21` `HOSTS=("$INGRESS_HOST" "minio.$INGRESS_HOST" "grafana.$INGRESS_HOST")`;
  `:44-45` creates `forum-tls` in `$K8S_NAMESPACE` only.
- **Change:**
  ```bash
  # scripts/mkcert-tls.sh
  -HOSTS=("$INGRESS_HOST" "minio.$INGRESS_HOST" "grafana.$INGRESS_HOST")
  +# gomx.local = Architecture B's ingress host in the shared A/B benchmark cluster (same cert, same CA).
  +HOSTS=("$INGRESS_HOST" "minio.$INGRESS_HOST" "grafana.$INGRESS_HOST" "${GOMX_INGRESS_HOST:-gomx.local}")
  ```
  and after the `forum-tls` block:
  ```bash
  +  # B's namespace gets its own copy (Secrets are namespaced); created only when the namespace exists.
  +  if kubectl get namespace "${GOMX_NAMESPACE:-gomx}" >/dev/null 2>&1; then
  +    kubectl -n "${GOMX_NAMESPACE:-gomx}" create secret tls gomx-tls --cert="$TLS_DIR/tls.crt" --key="$TLS_DIR/tls.key" \
  +      --dry-run=client -o yaml | kubectl apply -f - >/dev/null
  +    ok "Secret gomx-tls created/updated in namespace ${GOMX_NAMESPACE:-gomx}"
  +  fi
  ```
  Add `GOMX_INGRESS_HOST`/`GOMX_NAMESPACE` defaults to `lib.sh load_env` (`gomx.local`, `gomx`). Update
  `k8s/ingress/ingress.yaml:2` comment and `docs/runbooks/wsl-minikube-setup.md` §4 (Windows hosts
  file gets `127.0.0.1 gomx.local`; the mkcert CA is already in the Windows store).
- **Verification:** `scripts/mkcert-tls.sh --force` then `openssl x509 -in k8s/ingress/tls/tls.crt -noout -text | grep DNS` lists 4 SANs;
  after B is deployed: `curl -sI --resolve gomx.local:443:$(minikube -p forum ip) https://gomx.local/healthz` → `HTTP/2 200`.
- **Effort:** 20 min. **Pitfall:** run `mkcert-tls.sh --force` **before** `make mk-deploy` (deploy.sh creates `forum-tls` from the files) and re-run it after `kubectl apply -k …/bench` creates the `gomx` namespace.

#### A-05 — Common resource profile + fixed replicas on A

- **Invariant:** F3.
- **Current code:** `k8s/backend/deployment.yaml:49-50`; `k8s/backend/hpa.yaml` (applied by `deploy.sh`).
- **Change:**
  ```yaml
  # k8s/backend/deployment.yaml
  -            requests: { cpu: "150m", memory: "256Mi" }
  -            limits:   { cpu: "750m", memory: "512Mi" }
  +            # A/B common profile (AB-ALIGNMENT-INSTRUCTIONS-*.md §2): identical on gomx's bench overlay.
  +            requests: { cpu: "250m", memory: "256Mi" }
  +            limits:   { cpu: "1000m", memory: "512Mi" }
  ```
  Pool math note in the deployment header stays valid (3 × 30 ≤ 100). Day-0 fixed replicas are a
  **runtime** step recorded in `meta.json`, not a manifest change:
  ```bash
  kubectl -n forum-dotnet delete hpa backend          # Day 0 only; `make mk-deploy` re-creates it
  kubectl -n forum-dotnet scale deploy/backend --replicas=2
  ```
  (For later HPA classes both sides use `hpa.yaml` as is — 1–3 @ 70 % with A's behaviour block; B
  copies it.)
- **Verification:** `kubectl -n forum-dotnet get deploy backend -o jsonpath='{.spec.template.spec.containers[0].resources}'`;
  `kubectl -n forum-dotnet get hpa` → none; 2 Ready pods.
- **Effort:** 10 min + rollout.

#### A-06 — Apply B's ServiceMonitor into A's Prometheus

- `kubectl apply -f ../gomx/deploy/monitoring/servicemonitor.yaml` (ns `gomx`, port `metrics`, 15 s). Works
  because A's Prometheus selects ServiceMonitors in all namespaces (`values-kube-prometheus-stack.yaml:25-27`);
  B's netpol admits `monitoring` → 9090 (`networkpolicy.yaml:42-47`). Verify in
  `make mon-check` or `http://localhost:19090/targets` (via `make tunnels`) that `gomx/gomx` is UP.
  Add the apply line to a new `scripts/gomx-bench-deploy.sh` (A-08). **Effort:** 10 min.

#### A-07 — Tracing off on A during measured runs — **IMPLEMENTED (Option 2) 2026-10-17**

Landed: `Observability:TracingEnabled` (env `Observability__TracingEnabled=false`) in `ObservabilityExtensions.cs` skips `.WithTracing` entirely (metrics untouched); `Forum.Api.Tests/Hosting/ObservabilityTracingToggleTests.cs` (2 tests, green; 53/53 Api tests green). Bench: `kubectl -n forum-dotnet set env deploy/backend Observability__TracingEnabled=false`; restore with `Observability__TracingEnabled-`. Option 1 is no longer needed.

- **Invariant:** F8. A currently traces 100 % of requests (no sampler) and exports every span.
- **Option 1 (no rebuild, verify first):** the OpenTelemetry .NET SDK reads `OTEL_TRACES_SAMPLER`;
  set `OTEL_TRACES_SAMPLER=always_off` on the backend for runs:
  `kubectl -n forum-dotnet set env deploy/backend OTEL_TRACES_SAMPLER=always_off` and confirm in Grafana
  → Tempo that no new `forum` traces appear after a smoke. **UNCERTAIN until checked** on the pinned
  SDK (1.16); if traces still appear use Option 2.
- **Option 2 (deterministic, ~10 lines, needs image rebuild):** in
  `backend/src/Bootstrap/Forum.Api/Extensions/ObservabilityExtensions.cs` wrap the `.WithTracing(...)`
  call: `var tracingEnabled = builder.Configuration.GetValue("Observability:TracingEnabled", true);`
  and only call `.WithTracing` when true (metrics stay). Bench: `Observability__TracingEnabled=false` via
  `kubectl set env`. Add a unit test in `Forum.Api.Tests` asserting no `TracerProvider` is registered
  when the key is false.
- Record whichever is used in `meta.json` (`tracing: "off (OTEL_TRACES_SAMPLER=always_off)"`). Restore
  after the runs (dashboards/Tempo demos need it).
- **Effort:** 15 min (Option 1) / 45 min + rebuild (Option 2).

#### A-08 — `scripts/gomx-bench-deploy.sh` (build + deploy B into the shared cluster) — **IMPLEMENTED 2026-10-17**

Landed as written below plus: an **A-hosted copy of the bench overlay** in `k8s/gomx-bench/` (identical content to the B document §B-05) used when Hubert's `deploy/k8s/bench/` is absent. Two kustomize facts found live: (1) a base outside the overlay root cannot be referenced (absolute paths refused; relative `../../../gomx/...` mangles the base) → the script pre-renders gomx's base into `k8s/gomx-bench/gomx-base.rendered.yaml` (gitignored) and the overlay consumes that file; (2) trailing comments after flow mappings in a strategic-merge patch break kustomize v5.8.1 (`yaml: line N: could not find expected ':'`) — comments live on their own lines. `make gomx-deploy [ARGS=--reset-db]`.

A small A-side script so Day 0 is reproducible (B's `task k8s:*` targets assume podman and are not
used here):
```bash
#!/usr/bin/env bash
# Deploys Architecture B (gomx, bench overlay) into the shared minikube profile. Usage:
#   scripts/gomx-bench-deploy.sh [path-to-gomx] [--reset-db]
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"; load_env
GOMX_DIR="${1:-$REPO_ROOT/../gomx}"; NS="${GOMX_NAMESPACE:-gomx}"
SHA="$(git -C "$GOMX_DIR" rev-parse --short HEAD)"; [[ -n "$(git -C "$GOMX_DIR" status --porcelain)" ]] && SHA="$SHA-dirty"
step "Building gomx image inside minikube's docker ($SHA)"
eval "$(mk docker-env)"
docker build -f "$GOMX_DIR/ContainerFile.dev" -t gomx:bench -t "gomx:bench-$SHA" --build-arg GIT_SHA="$SHA" "$GOMX_DIR"
eval "$(mk docker-env -u)"
step "Applying bench overlay (replicas 1 → seeds)"
kubectl apply -k "$GOMX_DIR/deploy/k8s/bench"
bash "$REPO_ROOT/scripts/mkcert-tls.sh" >/dev/null          # creates gomx-tls now that the ns exists
kubectl -n "$NS" rollout status statefulset/postgres --timeout=180s
kubectl -n "$NS" rollout status deploy/gomx --timeout=900s   # startupProbe allows up to 15 min of seeding
step "Waiting for the bench seed"
until kubectl -n "$NS" logs deploy/gomx --tail=200 2>/dev/null | grep -q 'benchmark seed complete'; do sleep 5; done
kubectl -n "$NS" scale deploy/gomx --replicas=2
kubectl -n "$NS" rollout status deploy/gomx --timeout=300s
kubectl apply -f "$GOMX_DIR/deploy/monitoring/servicemonitor.yaml"
ok "gomx up: https://gomx.local (resolve to $(mk ip)); /stats: $(curl -sk --resolve "gomx.local:443:$(mk ip)" https://gomx.local/stats)"
```
`--reset-db` runs the DROP/CREATE from the B document §B-05 before re-applying. If Hubert's branch lacks
the overlay, create `deploy/k8s/bench/` in the local gomx checkout from the B document (it is complete)
and tell him to merge it. **Effort:** 45 min (+ image build time, first build ~5–10 min: bun install + go build).

#### A-09 — Day-0 static metrics on both sides (procedure, no code) → §5.7

### P1 — next classes

#### A-10 — Global keyset feed endpoint (J1 = home feed on both) — ~½ day
- Add `GET /api/content/threads/feed` (or make `categoryId` optional on `GetThreadFeedEndpoint.cs:19-29`):
  SQL over `thread_feed_v` without the category filter, **public categories only** (JOIN categories
  visibility — see A-12), cursor `ThreadFeedCursor` unchanged. New migration adding
  `ix_threads_feed_global (is_pinned DESC, created_on_utc DESC, id DESC) WHERE is_deleted = false`
  (today only the per-category `ix_threads_feed` exists, `InitialContent.cs:209-214`). SPA home
  switches from the client-side k-way merge (`lib/feed/feed-merge.ts`) to the endpoint; keep the merge
  code for the thesis narrative or delete it. Tests: `ContentFlowTests` keyset-without-dupes across
  categories; k6 `targets/a.js` J1 → global feed when `MIX_VERSION ≥ v1`. B's J1 then uses `GET /`.
#### A-11 — Benchmark social seed (D7) — ~½–1 day
- `SeedPlan.cs` Benchmark init-props: 2400 accepted friendships (ring + chords 1/7/40 over the 800
  users, mirroring `seedbench.go:306-326`), 80 pending, 2000 DMs over ring pairs; `SocialSeeder.cs:68-71`
  no longer no-ops. Needed for J9; unblocks forum-spec OQ-7.
#### A-12 — Close the private-category read gap (found in this audit) — ~½ day
- Content read handlers (`GetThreadFeed`, `GetThreadDetail`, `GetCommentTree`, `SearchThreads`,
  `GetUserThreads`) do not check `Visibility.Private`. Add the owner-or-`moderate`-or-ACL check used by
  `CreateThread.cs:87-96` (via `IContentVisibility`/`CategoryAccessReader`) → 404 for anonymous/non-members
  (A's 404→403→422 order). Integration test: anonymous read of a private-category thread → 404.
  Measured journeys are public-only, so no benchmark impact; it is a security fix and removes the
  fairness asymmetry D-17.
#### A-13 — J4 (+ image) and J8/J9 on both targets — ~1 day
- `targets/b.js`: `J4` (`POST /posts` into `postableSlug(userIndex)`), upload arm (`POST /uploads` →
  202 → poll `/uploads/{id}/status` → `attachments=`), `J9` (`POST /messages/{peer}`); `targets/a.js`
  J9 after A-11. Mix `v1` = forum-spec §9.3 once Hubert signs J-4.
#### A-14 — Shared selective word bank (OD-6) — ~2 h both sides
- Export a ~300-word Zipf-weighted bank as `load/k6/lib/wordbank.json` consumed by `SeedText.cs`
  (embedded copy), `assets.js` and B's seeder; reseed both.
#### A-15 — HPA classes
- Re-enable `hpa.yaml` on A; B copies A's HPA (1–3 @ 70 %, behaviour block). Keep the 9c lessons:
  probe timeouts, no Completed Job pods with the app label, CPU-init grace.

### P2
- A-16 Makefile targets `lighthouse`, `artifact-size`, `gomx-deploy`, `bench-b` (wrappers).
- A-17 Pin trivy version in `security-scan.sh` (`require_cmd` + version check against `TRIVY_VERSION`).
- A-18 Namespace PSS labels for `gomx` (cosmetic parity).

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

## 5. Day-0 runbook (2026-10-17) — real numbers tomorrow

> **Executed 2026-10-17 02:05–03:20 (night run):** steps 5.1–5.5 and 5.7 completed against the unmodified gomx `master` @ `8b23bc1` (Hubert's branch not yet available). Results + the deviation list for that run: `thesis/results/DAY0-2026-10-17.md`; archives `thesis/results/A/20261017-0205-demo/`, `thesis/results/B/20261017-0240-demo/`, static metrics `thesis/results/static/2026-10-17/`. Rerun 5.3 → 5.5 with `make gomx-deploy ARGS=--reset-db` once the `ab-benchmark` branch lands. Found during execution: deploy.sh's infra wait can time out right after `minikube start` while the hostpath storage-provisioner restarts (PVCs bind late) — simply re-run `make mk-deploy …`; Lighthouse on WSL still leaves literal `C:\Users\…\lighthouse.*` dirs in the repo root (delete them; they are not in .gitignore).

All commands from `~/projects/forum-dotnet` in WSL. Budget: ~5–6 h wall clock including builds. Keep
`make tunnels` **off** during measured runs (its forwards are fine, but Grafana browsing adds CPU to
the node).

### 5.1 Cluster up, A data wiped (PG 17 → 18), TLS with gomx.local
```bash
minikube -p forum start                       # profile exists (8 CPU / 10 GiB); ~2 min
make mk-down                                  # deletes ns forum-dotnet AND wipes hostpath PVC data (PG17 → 18 needs this)
scripts/mkcert-tls.sh --force                 # after A-04: 4 SANs; re-run later for gomx-tls
make mk-deploy ARGS=--seed-benchmark          # images (git-<sha>-dirty), secrets, migrate Job, Benchmark seed Job
make mon-up                                   # A's monitoring (Prometheus/Grafana/Loki/Alloy/Tempo), ~3 min
make mon-check
```
Expected: all `forum-dotnet` pods Ready; `kubectl -n forum-dotnet exec postgres-0 -- psql -U forum -d forum_net -tAc 'select count(*) from forum_identity.users'` → `800`.

### 5.2 A bench profile (recorded in meta.json)
```bash
kubectl -n forum-dotnet delete hpa backend
kubectl -n forum-dotnet scale deploy/backend --replicas=2
kubectl -n forum-dotnet set env deploy/backend Observability__TracingEnabled=false  # A-07 (implemented); restore: ...TracingEnabled-
kubectl -n forum-dotnet rollout status deploy/backend
kubectl -n forum-dotnet get deploy backend -o jsonpath='{.spec.template.spec.containers[0].resources}'   # 250m/256Mi → 1000m/512Mi (A-05)
```
(The limiter raise to 1 000 000 is done by `bench-run.sh` per run and restored.)

### 5.3 B into the same cluster
```bash
cd ../gomx && git fetch && git checkout ab-benchmark && git log -1 --oneline && cd ../forum-dotnet
make gomx-deploy                              # A-08: build in minikube docker, apply overlay (Hubert's or k8s/gomx-bench), wait seed, scale 2, ServiceMonitor
curl -sk --resolve gomx.local:443:$(minikube -p forum ip) https://gomx.local/stats     # {"posts":1600,"comments":9000,...}
curl -sk --resolve gomx.local:443:$(minikube -p forum ip) -o /dev/null -w '%{http_version} %{http_code}\n' https://gomx.local/c/general   # 2 200
kubectl -n gomx get pods                       # gomx ×2 Ready, postgres-0, redis, minio
```
If Hubert's branch is missing B-05: create `../gomx/deploy/k8s/bench/*` from the B document (verbatim)
and continue. If B-01/B-02/B-04 are missing: continue anyway and **record** in meta (`seed_deviations`
+ `behaviour_deviations: ["page size 8/10", "friend fan-out on", "in-app gzip on"]`) — tomorrow's
numbers are labelled preliminary either way.

### 5.4 Gate: smoke on both (0 % failures)
```bash
scripts/run-load-test.sh smoke https://forum.local
TARGET=b scripts/run-load-test.sh smoke https://gomx.local
```
Both must print `all thresholds passed`, `failed=0.00%`, and the journey table with seven rows.
Fix the adapter before any measured run — a 303-counted-as-failure or a 404 on `/u/{id}` is an
adapter bug, not a result.

### 5.5 Measured runs — sequential, idle side scaled to 0
```bash
# A first
kubectl -n gomx scale deploy/gomx --replicas=0
make bench ARGS="demo --repeats 3"                       # ~45 min: warm-up smoke + 3× demo (7 min) + cool-downs
kubectl -n gomx scale deploy/gomx --replicas=2 && kubectl -n gomx rollout status deploy/gomx
# then B
kubectl -n forum-dotnet scale deploy/backend --replicas=0
make bench-b ARGS="demo --repeats 3"                     # = TARGET=b scripts/bench-run.sh (A-03) → thesis/results/B/
kubectl -n forum-dotnet scale deploy/backend --replicas=2
```
Postgres/Redis/RabbitMQ/MinIO of the idle side stay up (they are part of the namespace footprint
but idle; recorded). If time allows, add one `stress` each (informational). **Do not** run the two
stacks' load concurrently. Repeats: 3 tomorrow, N ≥ 10 interleaved A,B,A,B in later classes.

### 5.6 Pod sets for H1 (document with the results)
- **A namespace `forum-dotnet`:** backend ×2 (measured app), frontend ×1 (SSR counterpart of B's
  in-pod rendering), postgres-0, rabbitmq-0, minio-0 (+ Completed Jobs, excluded).
- **B namespace `gomx`:** gomx ×2 (app incl. River workers + WS hub), postgres-0, redis, minio-0.
- Prometheus queries: `sum by (pod) (rate(container_cpu_usage_seconds_total{namespace="<ns>", container=""}[2m]))`
  and `container_memory_working_set_bytes{namespace="<ns>", container=""}` (minikube cAdvisor exports
  pod-slice series only — `container=""`). Report app pods and whole namespace separately.

### 5.7 Static metrics (both repos, same binaries, same day)
```bash
# trivy (0.72.0) — paired, same flags
scripts/security-scan.sh; scripts/security-scan.sh ../gomx          # trivy-report.json, trivy-report-gomx.json
# cloc — whole repo, then core scope (generated/out-of-core excluded)
npx -y cloc --vcs=git --json --out=.bench/cloc-A-whole.json .
npx -y cloc --vcs=git --json --out=.bench/cloc-A-core.json \
  --exclude-dir=Migrations,design-reference,src-tauri,.next,node_modules,thesis,docs,k8s,load,scripts .
npx -y cloc --vcs=git --json --out=.bench/cloc-B-whole.json ../gomx
npx -y cloc --vcs=git --json --out=.bench/cloc-B-core.json \
  --exclude-dir=db,templbook,templdesign,storyimports,static,src-tauri,loadtest,bruno,deploy,scripts,docs \
  --not-match-f='(_templ\.go|zz_generated_.*\.go|_story\.go)$' ../gomx
# artifact / image sizes
(cd frontend && npm run build) && make images && scripts/artifact-size.sh      # .bench/artifact-metrics.txt
docker image inspect gomx:bench -f '{{.Size}}'                                 # bytes; B's binary+static:
cid=$(docker create gomx:bench) && mkdir -p .bench/gomx && docker cp "$cid:/app/bin/server" .bench/gomx/ \
  && docker cp "$cid:/app/static" .bench/gomx/ && docker rm "$cid" && du -b .bench/gomx/server .bench/gomx/static/*.{css,js}
# Lighthouse — A (local prod build vs local API, Development seed) and B (image, in-memory demo seed)
make infra-up && make api ARGS=--migrate && make seed && (make api &) && sleep 15 && scripts/lighthouse.sh
docker run --rm -d --name gomx-lh -p 8080:8080 -e GOMX_SKIP_VITALS=1 gomx:bench && sleep 3
( cd ../gomx && TEMP=/tmp TMP=/tmp CHROME_PATH=$PWD/../forum-dotnet/.bench/chrome/chrome-wrapper \
  npx -y @lhci/cli --config=lighthouserc.json collect && npx -y @lhci/cli --config=lighthouserc.json upload ) \
  && node scripts/lighthouse-metrics.mjs ../gomx/.bench/lighthouse .bench/lighthouse-metrics-B.txt
docker rm -f gomx-lh
```
Notes: B's image includes `static/` (bundles) so `ci-artifact-metrics.sh`'s inputs are recoverable
without Go/bun. Both Lighthouse audits run against **demo-scale** data (A: 10 threads; B: 24 posts) —
pick threads with similar comment counts when quoting LCP; record both page URLs. The A audit is
cross-origin (`:3000` → `:5099`) and therefore pays CORS preflights the cluster does not (D-18).

### 5.8 Archive + deviations table
- `thesis/results/A/<stamp>-demo/`, `thesis/results/B/<stamp>-demo/` (bundles), `.bench/*` copied into
  `thesis/results/static/<date>/` (trivy JSONs, cloc JSONs, artifact metrics, lighthouse metrics + HTML).
- Deviations for the report: §6 below + `meta.json` `seed_deviations`/`behaviour_deviations` + the
  Day-0 mix version `v0-day0` (no J4/upload/J8/J9). Label every number **preliminary**.

### 5.9 Fallback if B does not come up in the cluster
1. **B in plain docker on the WSL host** (unequal environment → "indicative", static + Lighthouse unaffected):
   ```bash
   docker network create abfb; docker run -d --name pg18 --network abfb -e POSTGRES_USER=gomx -e POSTGRES_PASSWORD=gomx -e POSTGRES_DB=gomx postgres:18-alpine \
     -c shared_buffers=256MB -c effective_cache_size=512MB -c max_connections=100
   docker run -d --name gomx --network abfb -p 8080:8080 --cpus=1 --memory=512m \
     -e DATABASE_URL='postgres://gomx:gomx@pg18:5432/gomx?sslmode=disable' -e GOMX_DB_MAX_CONNS=30 -e GOMX_SEED=bench \
     -e GOMX_AUTH_RATELIMIT=0 -e GOMX_COMPRESS=0 -e GOMX_NOTIFY_FRIENDS_ON_POST=0 gomx:bench
   docker logs -f gomx | grep -m1 'benchmark seed complete'
   TARGET=b scripts/run-load-test.sh smoke http://localhost:8080 ; TARGET=b scripts/run-load-test.sh demo http://localhost:8080
   ```
   (`--cpus=1 --memory=512m` mirrors the pod limit; no ingress/TLS/gzip → record.)
2. **If the B image does not build** (bun/go network issues): present the **static metrics + Lighthouse
   + A's demo** and B's own `forum.ts` smoke numbers from Hubert's machine, clearly labelled
   non-comparable; the comparable load numbers move to the next class.
3. **If the harness adapter is late**: run J1/J2/J6/J7 only (anonymous reads) on both by setting
   `MIX = [['J1',45],['J2',30],['J6',15],['J7',10]]` — still a valid journey comparison, labelled `v0-reads`.

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
