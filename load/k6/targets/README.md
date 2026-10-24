# k6 target adapters (A/B harness)

`main.js` owns the experiment (profiles, journey mix + think time, journey metrics, thresholds,
summary blocks). A target adapter owns one wire format. Select with `-e TARGET=a|b`.

| Target | File | System | Default BASE_URL |
|---|---|---|---|
| `a` | `a.js` | forum-dotnet — .NET JSON API + Next.js CSR SPA | `https://forum.local` |
| `b` | `b.js` | gomx — Go + Templ/HTMX SSR monolith | `https://gomx.local` |

The wire contract each adapter encodes is pinned in
`docs/architecture/AB-ALIGNMENT-INSTRUCTIONS-{A,B}.md` §4 (identical in both documents).

## Interface (every adapter exports)

```js
export const NAME;              // 'a' | 'b'
export const DEFAULT_BASE_URL;
export const SEARCH_WORDS;      // words guaranteed to hit the target's seeded corpus (J6)
export const ENDPOINTS;         // `endpoint` tag values → informational per-endpoint thresholds + summary table
export function hosts(ingressIp);                       // k6 `hosts` pin map ({} when no ip)
export function setup(baseUrl, poolSize, loginStaggerMs);
//   → { sessions: [{ auth, userIndex }], spaces: [{ key, slug }], threadsBySpace: { [key]: [{ id, ownerId }] }, devMode }
//   `auth` is whatever the target needs per authenticated request (A: bearer token, B: Cookie header value).
//   threadsBySpace holds EVERY public thread of the space in feed order (pinned, then newest) — both adapters
//   crawl their feeds to the end (harness v1). Setup-time requests are tagged endpoint=setup.
export const journeys = { J1, J2, J3, J4, J5, J6, J7, J9, nav };
//   each: (baseUrl, data, session, pick) → void, reporting via lib/track.js `track(res, endpoint, okStatuses)`
//   pick = { space(data), thread(data) → { thread }, word(), commentBody() } (built by main.js)
//   J4 = thread WITH a photo (lib/assets.js makePng, J4_IMAGE_PX; A: presigned path + attach, B: ingest + 202/poll;
//        both post into the VU's per-user public space, untagged — h6);
//   J9 = direct message to a RANDOM seeded friend (A: friends from GET /api/social/friends in setup, one
//        get-or-create conversation per (user, friend) pair; B: friends from GET /messages, POST /messages/{friend}).
export function wsHold(baseUrl, data, session, holdMs, counters, pick, probe);
//   counters = { subscribed, notifications, errors, probesSent, probesReceived, probesLost, probesUnresolved,
//   receipt } (k6 metrics owned by main.js); probe = { intervalMs, timeoutMs } | null — every intervalMs the holding
//   VU writes ONE comment and times the write request's start until the push carrying that comment id reaches its
//   own socket (forum_ws_receipt); unanswered after timeoutMs = lost. Returns the ws.connect response or null.
```

Variants every adapter honours (harness h4): `AUTH_READS=1` — read journeys carry the session (A bearer, B
cookie; deviation D3's signed-in browsing); A additionally re-logs a VU in on a 401 (expired 15-min access
token; `forum_relogins`), B's DB sessions live 30 days. A-only: `REAL_SPA=1` — the read journeys replay the real
React pages' cold-cache request sets (deviation D1; section below).

Mix `v2` / harness `h4-2026-10-18`: J4 (3 %) and J9 (4 %) have weight on both targets, J8 holds carry receipt
probes, and the seeders' word bank is the 300-word Zipf bank (`lib/assets.js` SEARCH_WORDS — the same list as
A's `SeedText.cs` and B's `seedbench.go`). v2 numbers are not comparable to v1.

Harness `h6-2026-10-24` (pre-measurement audit; mix `v2` unchanged):
- **semantic failures count**: `lib/track.js` records every tracked outcome per endpoint (`forum_check_ok` /
  `forum_check_failed{endpoint}`), `main.js` thresholds `checks{scenario:http}` on the pass/fail profiles, and
  `ab-round.sh` compares the accepted write counts with the dataset delta (comments, threads, DMs) — a 303 that
  rejected the write (B) or a row that never appeared now invalidates the run;
- **core latency** = every journey except J4 (`forum_journey_core_duration`): B's J4 waits ≥ 1 s for its upload
  poll by design (D12), which made B's all-journey p95 the p98 of its other journeys; the headline latency and the
  capacity knee read the core trend, J4 keeps its own row;
- A's **J2 and J7 fire their independent calls in parallel** (`http.batch`, like the SPA's simultaneous mounts;
  h5 serialized them = +1 RTT on A), A's **J4 is untagged and posts into the VU's per-user space** (B's
  `postableSlug` rule), A's **J9 messages a random friend** (B's rule; both seeds carry the same ring);
- capacity: **one session per VU** (`loginPool = CAP_MAX_VUS + 20`) and **50-j/s steps from 150 to 500**;
  `ab_window.py` reads app/db/ingress cores per hold window (`resources.json.steps` → the report's capacity table
  and `capacity-per-step-cpu.json`);
- `forum_ws_early_close` counts holds the server ended early (B's slow-consumer policy, pod loss);
- the B adapter requires a positive thread id in `Location` (`/thread/[1-9]\d*$`) and reads the DM rejection
  marker `?error=rejected` (thesis-v3-b); B's receipt-probe latency is bounded below by its own POST time (the
  probe write runs inside the socket's event loop) — stated next to D14.

Rules: every journey must exist on **both** adapters before it gets weight in `main.js` `MIX`
(bump `MIX_VERSION` when weights change, `HARNESS_VERSION` when adapter behaviour changes); never follow
redirects on write journeys (B answers 303 — a followed redirect re-renders the whole page and double-counts
work); A/B parity of what one journey *does* matters more than parity of request counts (1 SPA journey = N
calls by design).

Wire rules shared by both adapters (harness v1, `lib/wire.js`):
- every request sends `Accept-Encoding: gzip` — k6 sends none by default, a browser always does; both hosts are
  compressed by the same ingress-nginx (B's in-process gzip is off in its bench overlay);
- bodies a journey does not parse are discarded (`responseType: 'none'` — still fully downloaded and timed);
- the search words are ONE shared list (`lib/assets.js` `SEARCH_WORDS` = the word bank both seeders use);
- a state-changing journey must change state on both sides: A's reaction endpoints are idempotent, so
  `a.js` J5 tracks per-VU like state and always sends the direction that toggles (B's vote endpoint toggles
  server-side).

## `REAL_SPA=1` — the real SPA client (target `a` only; closes deviation D1)

By default `a.js` is an **idealized** SPA client: a journey issues only the page's core data calls (thread open =
detail + tree + like batch), so A's server cost per journey is a *lower bound* (AB-COMPARISON-v1.md §6.2 D1). With
`REAL_SPA=1` (`ab-round.sh --real-spa`; `run-load-test.sh` forwards it; recorded as `variant.real_spa` in the journeys
block, in `round.json` and in `meta-A.json`) every read journey issues **the request set the React page issues on a
cold React-Query cache** — every page a first visit, nothing reused from a previous journey — which is the *upper
bound*: the live SPA sits between the two (its `staleTime` tiers, `lib/api/stale-times.ts`, keep icons/avatars/lists
for 5 min across client-side navigations). The default path is untouched; B has no such distinction (every B journey
IS a full page), so a round pairing both variants reads "A ideal / A real / B".

Mechanics (`a.js`, the `spa` section): a **wave** = the requests one React render fires at once → one `http.batch()`;
the next wave waits for the responses it depends on (slug → feed → cards; detail → author stats, related search,
commenter avatars), like the browser's dependent fetches; the journey duration is the whole waterfall. Identical query
keys are deduped *within* a page as React Query does (the author's avatar rendered twice = one request; a card's
category-icon fallback shares the header's key). Fallback tiles render *while* the primary lookup loads
(`ThreadIcon` → `CategoryIcon` → `Monogram`), so a card's `thread_icon` and `category_icon` lookups are concurrent.
Fan-out concurrency: k6's `batchPerHost` (default 6 = Chrome's HTTP/1.1 per-host connections) is raised to 128 over
`https` — the browser multiplexes the whole wave on one HTTP/2 connection, ingress-nginx's default
`http2_max_concurrent_streams` — override with `REAL_SPA_BATCH_PER_HOST` (recorded as
`variant.real_spa_batch_per_host`).

Derived from the frontend sources on 2026-10-24 (paths under `frontend/src/`; "wave n" = issued after wave n-1's
responses; the endpoint tag is the summary-table row). Reads are anonymous unless `AUTH_READS=1`.

| Page (journey) | Wave | Request | Issued by | Endpoint tag |
|---|---|---|---|---|
| `/c/[slug]` (J1) | 1 | `GET /api/content/categories/{slug}` | `app/c/[slug]/page.tsx` → `useCategory(slug)` (`lib/hooks/use-content.ts`) — the route only knows the slug | `category_by_slug` |
| | 1 | `GET /api/content/categories` | `components/layout/CategorySidebar.tsx` → `useCategories()` | `categories` |
| | 2 | `GET /api/content/threads?categoryId=…&limit=20` | `useThreadFeed(categoryId)` — enabled once the slug resolved | `feed` |
| | 2 | `GET /api/files?targetType=category_icon&targetId=…` × (sidebar rows ∪ current category) | header `CategoryIcon` + one per sidebar row (`components/ui/CategoryIcon.tsx`); same key → the current category is one lookup | `category_icon` |
| | 3 | `GET /api/engagement/reactions/batch?targetType=thread&targetIds=<sorted ids>` | `useReactionBatch("thread", threadIds)` (`lib/hooks/use-reactions.ts`) | `reactions_batch` |
| | 3 | `GET /api/files?targetType=thread_icon&targetId=…` × 20 | `components/thread/ThreadCard.tsx` → `components/ui/ThreadIcon.tsx`; its `CategoryIcon` fallback = the current category (deduped) | `thread_icon` |
| | 4–5 (30 %) | feed page 2 by cursor, then the batch over **all 40** loaded ids (the batch query is keyed on the whole list) + 20 new `thread_icon` lookups | `LoadMoreButton` → `fetchNextPage()`; `useReactionBatch` re-keys | `feed`, `reactions_batch`, `thread_icon` |
| | — | *not issued*: avatars (cards show `@username` as text), per-card single reaction GETs (`ReactionButton covered`), the MOST POPULAR card (same keys) | | |
| `/t/[id]` (J2) | 1 | `GET /api/content/threads/{id}` | `app/t/[id]/page.tsx` → `useThread(id)` | `thread` |
| | 1 | `GET /api/content/threads/{id}/comments` | page-level `useComments(id)` (mounts at once; `components/comments/CommentSection.tsx` shares the key) | `comments` |
| | 2 | `GET /api/engagement/users/{ownerId}/stats` | author card → `useUserStats(thread.data?.ownerId)` (`lib/hooks/use-user-stats.ts`) | `user_stats` |
| | 2 | `GET /api/files?targetType=thread&targetId={id}` | attachment rail → `useQuery(queryKeys.filesByTarget("thread", id))` | `attachments` |
| | 2 | `GET /api/content/search?q=<first tag>&limit=5` | RELATED THREADS → `useSearchThreads(firstTag, 5)` — only when the thread has a tag | `related_search` |
| | 2 | `GET /api/engagement/reactions/thread/{id}` | the article's `ReactionButton` (not `covered`) → `useReactionSummary` | `reaction_get` |
| | 2 | `GET /api/files?targetType=thread_icon&targetId={id}` | article head `ThreadIcon` | `thread_icon` |
| | 2 | `GET /api/files?targetType=category_icon&targetId={categoryId}` | `ThreadIcon`'s `CategoryIcon` fallback (renders while loading) | `category_icon` |
| | 2 | `GET /api/files?targetType=avatar&targetId={ownerId}` | author card + byline `Avatar` (`components/ui/Avatar.tsx`) — one key | `avatar` |
| | 2 | `GET /api/engagement/reactions/batch?targetType=comment&targetIds=<sorted ≤ 100>` | `CommentSection` / page `useReactionBatch("comment", ids)` — skipped when there are no comments | `reactions_batch` |
| | 2 | `GET /api/files?targetType=avatar&targetId=…` × distinct commenters (owner deduped) | `components/comments/CommentNode.tsx` → `Avatar` | `avatar` |
| | 3 | `GET /api/files?targetType=category_icon&targetId=…` × distinct categories of the ≤ 4 related rows, unseen only | RELATED THREADS rows → `CategoryIcon` | `category_icon` |
| | — | *not issued*: comment `ReactionButton`s (`covered`); inline media `GET /api/files/{id}` (`MarkdownView` resolves `image:` tokens — the seeded bodies carry none and J2 draws from the setup pool, never from J4's threads); WS subscribe (frames, J8) | | |
| `/search?q=` (J6) | 1 | `GET /api/content/search?q=<word>&limit=20` | `app/search/page.tsx` → `useSearchThreads(query)`; no sidebar (`PageShell wide={false}`) | `search` |
| | 2 | reaction batch over the result ids (sorted) | `useReactionBatch("thread", resultIds)` | `reactions_batch` |
| | 2 | `thread_icon` × results, `category_icon` × distinct result categories | `ThreadCard` → `ThreadIcon` (+ fallback) | `thread_icon`, `category_icon` |
| `/u/[userId]` (J7) | 1 | `GET /api/engagement/users/{id}/stats` | `app/u/[userId]/page.tsx` → `useUserStats(userId)` | `user_stats` |
| | 1 | `GET /api/files?targetType=avatar&targetId={id}` | identity `Avatar` | `avatar` |
| | 1 | `GET /api/content/users/{id}/threads?limit=20` | `ActivityTimeline` → `useUserThreads` | `user_threads` |
| | 1 | `GET /api/content/users/{id}/comments?limit=20` | `ActivityTimeline` → `useUserComments` (second merge source) | `user_comments` |
| | 2 (`AUTH_READS`, not own profile) | `GET /api/social/friends?limit=50`, `GET /api/social/friends/requests`, `GET /api/social/blocks` | `components/social/ProfileSocialActions.tsx` (mounts once the stats landed) → `useFriends` / `useFriendRequests` / `useBlockedUsers` | `social_friends`, `social_requests`, `social_blocks` |
| | — | *not issued*: per-row icons/avatars (activity rows are badge + text) | | |
| `nav` (list spaces) | 1 | `GET /api/content/categories` | `components/layout/CategorySidebar.tsx` → `useCategories()` — the SPA has no categories page; the CATEGORIES sidebar (on `/` and `/c/[slug]`) is its "list spaces" surface | `categories` |
| | 2 | `GET /api/files?targetType=category_icon&targetId=…` × rows | one `CategoryIcon` per sidebar row | `category_icon` |
| signed-in chrome (`AUTH_READS=1`, every page) | 1 | `GET /api/social/notifications/unread-count` | `components/layout/TopNav.tsx` → `SocialNavLinks` → `useUnreadNotificationCount()` | `social_unread` |
| | 1 | `GET /api/social/conversations` | `SocialNavLinks` → `useConversations()` (messages badge) | `social_convos` |
| | 1 | `GET /api/files?targetType=avatar&targetId=<self>` | `UserMenu` → `Avatar(currentUser.id)` | `avatar` |

Deliberately **not replayed**: the app-shell boot — once per browser session, not per page: `POST /api/identity/refresh`
(`AuthProvider`'s silent restore; a 401 for anonymous visitors), the Next.js HTML/JS/CSS/font assets (served by the
frontend pod; the B adapter likewise fetches no static assets), the WebSocket ticket + connect of signed-in users (J8
models the socket) and the 30-s presence heartbeat (`PresenceHeartbeat`); the home page `/` (k-way merged feed over
every category — no B equivalent in the mix); presigned image bytes (no seeded file exists, and they never touch the
API). Write journeys (J3/J4/J5/J9) are unchanged: the SPA's mutations are the same single calls, and the invalidation
re-fetches they trigger are push-driven (WS), not part of the user action.
