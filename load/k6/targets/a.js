// Target adapter: Architecture A (forum-dotnet — JSON API + CSR SPA). See targets/README.md for the
// interface every adapter implements; main.js owns profiles, mix, journey metrics and the summary.
//
// Wire facts this adapter encodes (verified 2026-10-16): login by EMAIL (bench_user_NNNN@bench.local /
// Bench#Password1, Development fallback alice|bob|charlie@dev.local / Dev#Password1), bearer JWT,
// feed = GET /api/content/threads?categoryId=… (keyset cursor), thread open = detail + comment tree +
// reaction batch (the SPA's core 3-call pattern — decision D1: an idealized client, see the v1 report), search =
// GET /api/content/search?q=, profile = stats + user threads, WS = single-use ticket → /api/realtime/ws →
// subscribe category. Harness v1 (2026-10-17): the thread pool is the WHOLE public corpus (setup crawls every
// public feed to the end — same rule as targets/b.js), J5 always sends the state-changing direction, unparsed
// bodies are discarded, every request asks for gzip (lib/wire.js).
// Harness h4 (2026-10-18): J4 = thread WITH an image (initiate → presigned PUT → commit → create thread whose body
// embeds `![photo](image:<fileId>)` → attach, the SPA composer's exact sequence); J9 = direct message (open the
// conversation once per VU, then one POST per journey — the SPA keeps the conversation open); AUTH_READS=1 makes
// every read carry the bearer (signed-in browsing, deviation D3); a 401 on a write re-logs the VU in once (the
// 15-minute access token would otherwise end every profile longer than ~12 min — soak); J8 holds send RECEIPT
// PROBES: a comment written by the holding VU into its subscribed category, timed until the hub pushes the
// matching `comment created` envelope back (A's floor = the outbox relay poll, Messaging:PollIntervalMilliseconds).
// REAL_SPA=1 (2026-10-24, deviation D1): the read journeys replay the REAL React pages' cold-cache request sets
// instead of the idealized core calls — see the `spa` section below and the derived table in targets/README.md.
// Harness h6 (2026-10-24, pre-final audit): J2 fires detail + tree in PARALLEL and J7 stats + threads in parallel (the
// SPA mounts both queries at once over one HTTP/2 connection; h5 serialized them = +1 RTT on A per journey); J4 posts
// an UNTAGGED thread into the VU's per-user public space (B's `postableSlug` rule; tags are an A-only feature outside
// the shared core); J9 messages a RANDOM seeded friend (friends listed once per session in setup — B's /messages rule)
// with one conversation per (user, friend) pair; WS holds count early closes.

import encoding from 'k6/encoding';
import { sleep } from 'k6';
import { Counter } from 'k6/metrics';
import ws from 'k6/ws';

import {
  addReaction, apiBatch, attachFile, batchGet, commitUpload, createComment, createThread, initiateUpload,
  listCategories, listFriends, login, openDirectConversation, reactionBatch, realtimeTicket, removeReaction,
  searchThreads, sendMessage, spaPaths, threadFeed, uploadToPresignedUrl,
} from '../lib/api.js';
import { SEARCH_WORDS as SHARED_WORDS, makePng, threadBody, threadTitle } from '../lib/assets.js';
import { track } from '../lib/track.js';

export const NAME = 'a';
export const DEFAULT_BASE_URL = 'https://forum.local';
export const SEARCH_WORDS = SHARED_WORDS; // the word bank BOTH seeders draw from (identical list on B)
export const BOOSTED = false;

const AUTH_READS = __ENV.AUTH_READS === '1' || __ENV.AUTH_READS === 'true';
// Read journeys replay the real SPA pages' cold-cache waterfalls (deviation D1) — opt-in, see the `spa` section.
const REAL_SPA = __ENV.REAL_SPA === '1' || __ENV.REAL_SPA === 'true';
const J4_IMAGE_PX = Number(__ENV.J4_IMAGE_PX || 256); // 256×256 RGBA ≈ 262 KB — a small photo, same bytes on B

// Setup-time calls (login pool, pool discovery) are tagged with this endpoint so they never land in a journey's
// endpoint row of the summary table.
const SETUP = { endpoint: 'setup' };
const DISCARD = { discard: true };
const FEED_PAGE = 50; // GetThreadFeedQueryHandler.MaxLimit — the crawl's page size (setup only)
const CRAWL_MAX_PAGES = 200; // safety stop (the Benchmark seed needs ≤ 8 pages per category)

/** Re-logins forced by an expired access token (soak); informational, both targets expose the same counter. */
const relogins = new Counter('forum_relogins');

// Endpoint tag values (informational per-endpoint thresholds materialize them in the summary).
export const ENDPOINTS = [
  'login', 'categories', 'feed', 'thread', 'comments', 'reactions_batch', 'search',
  'comment_create', 'thread_create', 'reaction_add', 'reaction_remove', 'file_initiate', 'file_put',
  'file_commit', 'file_attach', 'dm_open', 'dm_send', 'probe_comment', 'user_stats', 'user_threads', 'ticket', 'setup',
  // REAL_SPA only (the real pages' secondary reads; zero rows unless the variant is on):
  'category_by_slug', // GET /api/content/categories/{slug} — the route resolves slug → id
  'thread_icon', 'category_icon', 'avatar', // GET /api/files?targetType=<name>&targetId= — one per rendered icon/avatar
  'attachments', // GET /api/files?targetType=thread&targetId= — the thread page's attachment rail
  'reaction_get', // GET /api/engagement/reactions/thread/{id} — the thread's own like button (not batch-covered)
  'related_search', // GET /api/content/search?q=<first tag>&limit=5 — RELATED THREADS panel
  'user_comments', // GET /api/content/users/{id}/comments — the profile's activity timeline (second source)
  'social_unread', 'social_convos', // TopNav badges, signed-in chrome (AUTH_READS)
  'social_friends', 'social_requests', 'social_blocks', // someone else's profile, signed-in (AUTH_READS)
];

/** k6 `hosts` pin map (forum.local resolves to 127.0.0.1 first on the WSL box — Windows-tunnel entry). */
export function hosts(ingressIp) {
  return ingressIp ? { 'forum.local': ingressIp, 'minio.forum.local': ingressIp } : {};
}

// Login pool: ordinary users only. Benchmark seeds 800 users; indices 0-1 admins, 2-11 moderators,
// 12-31 blocked (SeedPlan bands) — the pool starts at bench_user_0034 to keep every login an ordinary,
// non-blocked account (same index band as B's benchuser0034+).
const BENCH_USER_BASE = 33;
const BENCH_PASSWORD = 'Bench#Password1';
const DEV_USERS = ['alice', 'bob', 'charlie'];
const DEV_PASSWORD = 'Dev#Password1';

/** The `sub` claim of the access token = the user's ULID (the SPA decodes the same claims for "me"). */
function userIdFromToken(token) {
  try {
    const payload = JSON.parse(encoding.b64decode(token.split('.')[1], 'rawurl', 's'));
    return payload.sub || null;
  } catch {
    return null;
  }
}

/**
 * Runs an authenticated call; on 401 (expired access token) re-logs the VU in once and retries — the SPA's
 * silent-refresh equivalent for a client that holds no refresh cookie. `session` is the VU's own copy of the
 * setup record, so the refreshed token sticks for the rest of the run.
 */
function authed(baseUrl, session, call) {
  let res = call(session.auth);
  if (res.status === 401 && session.email) {
    const fresh = login(baseUrl, session.email, session.password);
    if (fresh.token) {
      session.auth = fresh.token;
      relogins.add(1);
      res = call(session.auth);
    }
  }
  return res;
}

/** Read option set: anonymous by default, the VU's bearer under AUTH_READS (D3 signed-in variant). */
function readOpts(session, extra = {}) {
  return AUTH_READS && session ? { ...extra, token: session.auth } : extra;
}

/**
 * Runs once (k6 setup): staggered logins + anonymous discovery of the public categories and of EVERY thread in
 * them (keyset crawl to the end, feed order: pinned first, then newest). Returns the target-neutral shape
 * { sessions, spaces, threadsBySpace, devMode } — main.js draws thread journeys uniformly from the whole public
 * corpus and ranks spaces by their crawled size (see its selection-helper comment).
 */
export function setup(baseUrl, poolSize, loginStaggerMs) {
  const probeEmail = `bench_user_${String(BENCH_USER_BASE + 1).padStart(4, '0')}@bench.local`;
  const probe = login(baseUrl, probeEmail, BENCH_PASSWORD);
  if (probe.status === 429) {
    throw new Error('Auth rate limiter hit on the FIRST login — raise RateLimiting__Auth__PermitLimit (bench-run.sh does) and retry.');
  }
  const devMode = probe.token === null;
  if (devMode) {
    console.warn('Benchmark users not found — falling back to the Development seed (alice/bob/charlie). Seed the Benchmark profile for measured runs.');
  }

  const sessions = devMode ? [] : [{
    auth: probe.token, userIndex: BENCH_USER_BASE, email: probeEmail, password: BENCH_PASSWORD,
    userId: userIdFromToken(probe.token),
  }];
  const wanted = devMode ? DEV_USERS.length : poolSize;
  for (let i = sessions.length; i < wanted; i++) {
    const email = devMode
      ? `${DEV_USERS[i]}@dev.local`
      : `bench_user_${String(BENCH_USER_BASE + 1 + i).padStart(4, '0')}@bench.local`;
    const password = devMode ? DEV_PASSWORD : BENCH_PASSWORD;
    const res = login(baseUrl, email, password);
    if (res.status === 429) {
      throw new Error(`Auth rate limiter hit after ${sessions.length} logins — raise RateLimiting__Auth__PermitLimit (bench-run.sh does) and retry.`);
    }
    if (res.token) {
      sessions.push({ auth: res.token, userIndex: BENCH_USER_BASE + i, email, password, userId: userIdFromToken(res.token) });
    }
    sleep(loginStaggerMs / 1000);
  }
  if (sessions.length === 0) {
    throw new Error('No logins succeeded — is the database seeded? (make seed ARGS="--benchmark --cluster")');
  }
  // J9 peers: the session's accepted friends (the Benchmark seed mirrors B's ring + chords, so every bench user has
  // ~6) — the same per-session discovery B does from its /messages inbox, one setup-tagged call per pool member.
  // D17 fix: filter out blocked accounts (bench_user_0013 through bench_user_0032 in the Benchmark seed) so J9
  // never targets an inactive user (which causes a 404 in OpenDirectConversation).
  // A's privacy default lets anyone message anyone, so a friendless account (Development seed) falls back to the
  // next pool member instead of skipping J9 — recorded as a warning, never silent.
  let fallbacks = 0;
  for (let i = 0; i < sessions.length; i++) {
    const res = listFriends(baseUrl, sessions[i].auth, SETUP);
    const peers = res.status === 200
      ? (res.json('items') || [])
          .filter((f) => {
            if (f.username && /^bench_user_00(1[3-9]|2[0-9]|3[0-2])$/.test(f.username)) {
              return false;
            }
            return Boolean(f.userId);
          })
          .map((f) => f.userId)
      : [];
    if (peers.length === 0) {
      fallbacks++;
      peers.push(sessions[(i + 1) % sessions.length].userId);
    }
    sessions[i].peers = peers;
  }
  if (fallbacks > 0) {
    console.warn(`A: ${fallbacks} of ${sessions.length} sessions have no friends — J9 falls back to the next pool member for them.`);
  }

  // Public categories only: the pool users are ordinary accounts, and a private-category 404 would
  // pollute http_req_failed with by-design rejections.
  const catRes = listCategories(baseUrl, SETUP);
  if (catRes.status !== 200) {
    throw new Error(`GET /api/content/categories failed (${catRes.status}) — cannot build the traffic pools.`);
  }
  const spaces = catRes.json().filter((c) => c.visibility === 'public').map((c) => ({ key: c.id, slug: c.slug }));
  if (spaces.length === 0) {
    throw new Error('No public categories found — seed the database first.');
  }

  // Thread pool: the whole public corpus (keyset crawl to the end). A depth-limited pool would sample
  // "the newest N" — and the two seeds correlate recency with comment volume differently (A: oldest = hottest;
  // B: cyclic ages), which made B open ~35% more comments per J2 under the Day-0 2×50 vs 2×20 pools. Over all
  // public threads the mean comments/thread is 5.73 (A) vs 5.76 (B). Feed items carry ownerId, which doubles as
  // the seeded-user pool for the profile journey (no separate user discovery call).
  const threadsBySpace = {};
  for (const space of spaces) {
    const threads = [];
    let cursor = null;
    for (let page = 0; page < CRAWL_MAX_PAGES; page++) {
      const res = threadFeed(baseUrl, space.key, cursor, FEED_PAGE, SETUP);
      if (res.status !== 200) {
        break;
      }
      const body = res.json();
      for (const item of body.items) {
        threads.push({ id: item.id, ownerId: item.ownerId });
      }
      cursor = body.nextCursor;
      if (!body.hasMore || !cursor) {
        break;
      }
    }
    threadsBySpace[space.key] = threads;
  }
  return { sessions, spaces, threadsBySpace, devMode, authReads: AUTH_READS, realSpa: REAL_SPA };
}

function batchFeedIds(baseUrl, body, session) {
  const ids = body.items.map((t) => t.id);
  if (ids.length > 0) {
    track(reactionBatch(baseUrl, 'thread', ids, readOpts(session, DISCARD)), 'reactions_batch');
  }
}

// J5 like state, per VU (each k6 VU has its own JS runtime and a fixed session — main.js vuSession). The A
// endpoints are idempotent, so a blind 50/50 PUT/DELETE made ~half of all J5 calls write-free no-ops on A while
// B's vote toggle ALWAYS writes; tracking the state makes every J5 a real state change on both sides. The rare
// seeded like by the same user (≈0.2% of pairs) only turns that first PUT into a no-op.
const likedByThisVu = new Map();

// J9 conversations per VU: opened on the first DM to a given friend (get-or-create, 200), reused afterwards — like
// the SPA's open chat dock. Keyed by (session user, friend) so a re-login never crosses conversations.
const conversationByPair = new Map();

// --- REAL_SPA — the cold-cache waterfall of the real React pages (deviation D1) --------------------------------------
//
// Opt-in (REAL_SPA=1; recorded as variant.real_spa in the journeys block). Every read journey then issues the HTTP
// requests the SPA page issues on a COLD React-Query cache (every page = a first visit), derived from the frontend
// sources on 2026-10-24 — targets/README.md tabulates each request with the component that issues it. Mechanics:
//   • a WAVE = the requests one render fires at once (independent queries + per-card icon/avatar lookups) → ONE
//     http.batch(); the next wave waits for the responses it depends on (slug → feed → cards; detail → author stats,
//     related search, avatars) exactly like the browser's dependent fetches. The journey duration = the waterfall.
//   • React Query dedupes identical query keys WITHIN a page: the author's avatar rendered twice is one request, a
//     card's category-icon fallback shares the header's key — `seen` reproduces that. Nothing is reused ACROSS
//     journeys (the SPA's 5-minute staleTime tiers are deliberately NOT modelled: this variant is the upper bound of
//     A's per-page server work, the default adapter the lower bound).
//   • Fallback tiles render WHILE the primary lookup loads (ThreadIcon → CategoryIcon → Monogram), so a card's
//     thread_icon and category_icon lookups are concurrent, never sequential.
//   • Signed-in chrome (AUTH_READS only): TopNav mounts the unread-notification count, the conversation list and the
//     user menu's own avatar on every page; someone else's profile adds the friends / requests / blocks queries.
//   • NOT replayed: the app-shell boot (POST /api/identity/refresh, static assets, WS connect — once per browser
//     session, J8 models the socket), the presence heartbeat, the home page's merged feed (no B equivalent).
// k6 caps http.batch() concurrency with batchPerHost (default 6 = Chrome's HTTP/1.1 per-host limit); main.js raises it
// to 128 over https, where the browser multiplexes the whole wave on one HTTP/2 connection (REAL_SPA_BATCH_PER_HOST).

/** One fan-out entry + its accounting (endpoint tag, accepted statuses). Reads carry the bearer only under AUTH_READS. */
function entry(baseUrl, path, endpoint, session, extra = {}, ok = [200]) {
  return { req: batchGet(baseUrl, path, endpoint, readOpts(session, extra)), endpoint, ok };
}

/** Issues one wave and tracks every response under its endpoint; responses come back in entry order. */
function wave(entries) {
  const responses = apiBatch(entries.map((e) => e.req));
  entries.forEach((e, i) => track(responses[i], e.endpoint, e.ok));
  return responses;
}

/** File lookups (`GET /api/files?targetType=&targetId=`) for the (type, id) pairs this page has not asked for yet. */
function fileLookups(baseUrl, session, seen, targetType, targetIds, endpoint) {
  const entries = [];
  for (const id of targetIds) {
    const key = `${targetType}:${id}`;
    if (!id || seen.has(key)) {
      continue;
    }
    seen.add(key);
    entries.push(entry(baseUrl, spaPaths.filesByTarget(targetType, id), endpoint, session, DISCARD));
  }
  return entries;
}

/** The signed-in chrome TopNav mounts on every page (components/layout/TopNav.tsx) — nothing for anonymous visitors. */
function chromeEntries(baseUrl, session, seen) {
  if (!AUTH_READS) {
    return [];
  }
  return [
    entry(baseUrl, spaPaths.unreadNotifications, 'social_unread', session, DISCARD),
    entry(baseUrl, spaPaths.conversations, 'social_convos', session, DISCARD),
    ...fileLookups(baseUrl, session, seen, 'avatar', [session.userId], 'avatar'),
  ];
}

/**
 * A list of ThreadCards (feed / search): ONE like-count batch over the ids the page holds (useReactionBatch sorts
 * them, ≤ 100) + each card's thread-icon lookup + the category-icon fallback per distinct category. Cards show
 * `@username` as text — no avatars — and their ReactionButtons are batch-covered (no single GETs).
 */
function cardEntries(baseUrl, session, seen, items, batchIds) {
  const sorted = [...batchIds].sort().slice(0, 100);
  return [
    ...(sorted.length > 0 ? [entry(baseUrl, spaPaths.reactionBatch('thread', sorted), 'reactions_batch', session, DISCARD)] : []),
    ...fileLookups(baseUrl, session, seen, 'thread_icon', items.map((t) => t.id), 'thread_icon'),
    ...fileLookups(baseUrl, session, seen, 'category_icon', items.map((t) => t.categoryId), 'category_icon'),
  ];
}

const spa = {
  /** /c/[slug] — J1 (app/c/[slug]/page.tsx). */
  category(baseUrl, data, session, pick) {
    const space = pick.space(data);
    const seen = new Set();
    // wave 1: the route resolves slug → category (useCategory) while the CATEGORIES sidebar lists every category
    // the visitor may see (useCategories); the feed and the batch are disabled until the id is known
    const w1 = wave([
      entry(baseUrl, spaPaths.categoryBySlug(space.slug), 'category_by_slug', session),
      entry(baseUrl, spaPaths.categories, 'categories', session),
      ...chromeEntries(baseUrl, session, seen),
    ]);
    if (w1[0].status !== 200) {
      return; // the page renders the error state — nothing below mounts
    }
    const categoryId = w1[0].json('id');
    const sidebarIds = w1[1].status === 200 ? w1[1].json().map((c) => c.id) : [];
    // wave 2: feed page 1 + the header's CategoryIcon + one CategoryIcon per sidebar row (the current category is
    // the same query key as the header → one lookup)
    const w2 = wave([
      entry(baseUrl, spaPaths.threadFeed(categoryId), 'feed', session),
      ...fileLookups(baseUrl, session, seen, 'category_icon', [categoryId, ...sidebarIds], 'category_icon'),
    ]);
    if (w2[0].status !== 200) {
      return;
    }
    const page1 = w2[0].json();
    // wave 3: the cards — like-count batch + per-card ThreadIcon (its category fallback is already in flight);
    // the MOST POPULAR card re-renders one of them (same keys, nothing new)
    wave(cardEntries(baseUrl, session, seen, page1.items, page1.items.map((t) => t.id)));
    // 30 % LOAD MORE ↓ (the ideal J1's cursor-follow): page 2, then the batch is RE-KEYED on the whole list — the
    // SPA re-requests the like counts of all 40 loaded cards — plus the 20 new cards' icon lookups
    if (Math.random() < 0.3 && page1.hasMore && page1.nextCursor) {
      const w4 = wave([entry(baseUrl, spaPaths.threadFeed(categoryId, page1.nextCursor), 'feed', session)]);
      if (w4[0].status === 200) {
        const page2 = w4[0].json();
        wave(cardEntries(baseUrl, session, seen, page2.items, [...page1.items, ...page2.items].map((t) => t.id)));
      }
    }
  },

  /** /t/[id] — J2 (app/t/[id]/page.tsx + components/comments/*). */
  thread(baseUrl, data, session, pick) {
    const { thread } = pick.thread(data);
    const seen = new Set();
    // wave 1: detail (useThread) + the comment tree (the page-level useComments; CommentSection shares its key)
    const w1 = wave([
      entry(baseUrl, spaPaths.thread(thread.id), 'thread', session),
      entry(baseUrl, spaPaths.comments(thread.id), 'comments', session),
      ...chromeEntries(baseUrl, session, seen),
    ]);
    if (w1[0].status !== 200) {
      return; // error state — the article, its panels and the comment section never mount
    }
    const detail = w1[0].json();
    const comments = w1[1].status === 200 ? w1[1].json() : [];
    const commentIds = comments.map((c) => c.id).sort().slice(0, 100); // = useReactionBatch('comment', ids)
    const firstTag = detail.tags && detail.tags.length > 0 ? detail.tags[0] : null;
    // wave 2: everything the rendered page fires at once — author-card stats, the attachment rail, RELATED THREADS
    // (search by the first tag, 5), the thread's own like state (its ReactionButton is not batch-covered), the
    // article's ThreadIcon + its category fallback, the author's Avatar (card + byline = one key), the comment
    // section's like batch and one Avatar per distinct commenter
    const w2entries = [
      entry(baseUrl, spaPaths.userStats(detail.ownerId), 'user_stats', session, DISCARD),
      entry(baseUrl, spaPaths.filesByTarget('thread', detail.id), 'attachments', session, DISCARD),
      ...(firstTag ? [entry(baseUrl, spaPaths.search(firstTag, 5), 'related_search', session)] : []),
      entry(baseUrl, spaPaths.reactionSummary('thread', detail.id), 'reaction_get', session, DISCARD),
      ...fileLookups(baseUrl, session, seen, 'thread_icon', [detail.id], 'thread_icon'),
      ...fileLookups(baseUrl, session, seen, 'category_icon', [detail.categoryId], 'category_icon'),
      ...fileLookups(baseUrl, session, seen, 'avatar', [detail.ownerId, ...comments.map((c) => c.ownerId)], 'avatar'),
      ...(commentIds.length > 0
        ? [entry(baseUrl, spaPaths.reactionBatch('comment', commentIds), 'reactions_batch', session, DISCARD)]
        : []),
    ];
    const w2 = wave(w2entries);
    // wave 3: the RELATED THREADS rows (≤ 4, the thread itself filtered out) each render a CategoryIcon — distinct,
    // not-yet-requested categories only
    const relatedIdx = w2entries.findIndex((e) => e.endpoint === 'related_search');
    if (relatedIdx >= 0 && w2[relatedIdx].status === 200) {
      const rows = (w2[relatedIdx].json('items') || []).filter((t) => t.id !== detail.id).slice(0, 4);
      wave(fileLookups(baseUrl, session, seen, 'category_icon', rows.map((t) => t.categoryId), 'category_icon'));
    }
  },

  /** /search?q= — J6 (app/search/page.tsx; no sidebar). */
  search(baseUrl, _data, session, pick) {
    const seen = new Set();
    const w1 = wave([
      entry(baseUrl, spaPaths.search(pick.word(), 20), 'search', session),
      ...chromeEntries(baseUrl, session, seen),
    ]);
    if (w1[0].status !== 200) {
      return;
    }
    const items = w1[0].json('items') || [];
    // wave 2: the result cards — like-count batch + per-card ThreadIcon + category fallback per distinct category
    wave(cardEntries(baseUrl, session, seen, items, items.map((t) => t.id)));
  },

  /** /u/[userId] — J7 (app/u/[userId]/page.tsx). */
  profile(baseUrl, data, session, pick) {
    const { thread } = pick.thread(data);
    const userId = thread.ownerId;
    const seen = new Set();
    // wave 1: the identity card (useUserStats + Avatar) and RECENT ACTIVITY (useUserThreads + useUserComments,
    // merged client-side) — four independent queries, all at mount; the activity rows are badge + text, no icons
    const w1 = wave([
      entry(baseUrl, spaPaths.userStats(userId), 'user_stats', session, DISCARD),
      ...fileLookups(baseUrl, session, seen, 'avatar', [userId], 'avatar'),
      entry(baseUrl, spaPaths.userThreads(userId), 'user_threads', session, DISCARD),
      entry(baseUrl, spaPaths.userComments(userId), 'user_comments', session, DISCARD),
      ...chromeEntries(baseUrl, session, seen),
    ]);
    // wave 2 (signed in, someone else's profile): ProfileSocialActions mounts once the stats landed and derives the
    // relationship from the friends / requests / blocks queries
    if (AUTH_READS && userId !== session.userId && w1[0].status === 200) {
      wave([
        entry(baseUrl, spaPaths.friends, 'social_friends', session, DISCARD),
        entry(baseUrl, spaPaths.friendRequests, 'social_requests', session, DISCARD),
        entry(baseUrl, spaPaths.blocks, 'social_blocks', session, DISCARD),
      ]);
    }
  },

  /** nav — the CATEGORIES sidebar (components/layout/CategorySidebar.tsx), the SPA's only "list spaces" surface. */
  nav(baseUrl, _data, session) {
    const seen = new Set();
    const w1 = wave([
      entry(baseUrl, spaPaths.categories, 'categories', session),
      ...chromeEntries(baseUrl, session, seen),
    ]);
    if (w1[0].status !== 200) {
      return;
    }
    // wave 2: one CategoryIcon per row
    wave(fileLookups(baseUrl, session, seen, 'category_icon', w1[0].json().map((c) => c.id), 'category_icon'));
  },
};

export const journeys = {
  /** J1 — space feed: page 1 + reaction batch over its cards (the SPA's real render); 30% follow the cursor. */
  J1(baseUrl, data, session, pick) {
    if (REAL_SPA) {
      spa.category(baseUrl, data, session, pick);
      return;
    }
    const space = pick.space(data);
    const first = threadFeed(baseUrl, space.key, null, 20, readOpts(session));
    if (!track(first, 'feed')) {
      return;
    }
    const body = first.json();
    batchFeedIds(baseUrl, body, session);
    if (Math.random() < 0.3 && body.hasMore && body.nextCursor) {
      const second = threadFeed(baseUrl, space.key, body.nextCursor, 20, readOpts(session));
      if (track(second, 'feed')) {
        batchFeedIds(baseUrl, second.json(), session);
      }
    }
  },

  /** J2 — thread open: detail + comment tree + reaction batch over the comments (SPA: sorted ids, ≤100). */
  J2(baseUrl, data, session, pick) {
    if (REAL_SPA) {
      spa.thread(baseUrl, data, session, pick);
      return;
    }
    const { thread } = pick.thread(data);
    // useThread + useComments mount together → both requests leave at once (one HTTP/2 connection); only the like
    // batch depends on the tree's ids. (h5 issued detail → tree sequentially: one extra RTT counted against A.)
    const [detail, tree] = apiBatch([
      batchGet(baseUrl, spaPaths.thread(thread.id), 'thread', readOpts(session, DISCARD)),
      batchGet(baseUrl, spaPaths.comments(thread.id), 'comments', readOpts(session)),
    ]);
    track(detail, 'thread');
    let batchIds = [thread.id];
    let batchType = 'thread';
    if (track(tree, 'comments')) {
      const comments = tree.json();
      if (comments.length > 0) {
        batchIds = comments.map((c) => c.id).sort().slice(0, 100); // = lib/hooks/use-reactions.ts useReactionBatch
        batchType = 'comment';
      }
    }
    track(reactionBatch(baseUrl, batchType, batchIds, readOpts(session, DISCARD)), 'reactions_batch');
  },

  /** J3 — top-level comment (replies risk the depth-5 422 on seeded depth-4 chains). */
  J3(baseUrl, data, session, pick) {
    const { thread } = pick.thread(data);
    const body = pick.commentBody();
    track(authed(baseUrl, session, (auth) => createComment(baseUrl, auth, thread.id, body, DISCARD)), 'comment_create', [200, 201]);
  },

  /**
   * J4 — thread with a photo, the SPA composer's sequence (ADR 0008 golden path + attach): initiate → presigned
   * PUT straight to MinIO → commit (backend stats the object + sniffs the magic bytes + reads the IHDR) → create
   * the thread with the inline-media token in its body → attach the file to the thread. One journey = one user
   * action ("post a thread with a picture"); B does the same with its server-side ingest + 202/poll (targets/b.js).
   * Placement and content follow B's rules so the write work is the same shape: the thread goes into the VU's
   * per-user public space (B: `postableSlug(userIndex)` — a membership rule A does not have, so the index rule is
   * mirrored) and carries NO tags (tags are an A-only feature outside the shared core; h5 added 1–3 tag upserts).
   */
  J4(baseUrl, data, session) {
    const space = data.spaces[session.userIndex % data.spaces.length];
    const png = makePng(J4_IMAGE_PX, J4_IMAGE_PX, Math.random);
    const initiate = authed(baseUrl, session, (auth) => initiateUpload(baseUrl, auth, 'image/png', png.size));
    if (!track(initiate, 'file_initiate', [200, 201])) {
      return;
    }
    const uploadUrl = initiate.json('uploadUrl');
    const fileId = initiate.json('fileId');
    if (!track(uploadToPresignedUrl(uploadUrl, png.bytes, 'image/png'), 'file_put')) {
      return;
    }
    if (!track(authed(baseUrl, session, (auth) => commitUpload(baseUrl, auth, fileId)), 'file_commit')) {
      return;
    }
    const body = `${threadBody(Math.random)}\n\n![photo](image:${fileId})\n`;
    const created = authed(baseUrl, session, (auth) =>
      createThread(baseUrl, auth, space.key, threadTitle(Math.random), body, []));
    if (!track(created, 'thread_create', [200, 201])) {
      return;
    }
    const threadId = created.json('threadId') || created.json('id');
    track(authed(baseUrl, session, (auth) => attachFile(baseUrl, auth, fileId, 'thread', threadId)), 'file_attach', [200, 201, 204]);
  },

  /** J5 — reaction toggle: always the state-changing direction (like the SPA button / B's vote toggle). */
  J5(baseUrl, data, session, pick) {
    const { thread } = pick.thread(data);
    if (likedByThisVu.get(thread.id)) {
      if (track(authed(baseUrl, session, (auth) => removeReaction(baseUrl, auth, thread.id, DISCARD)), 'reaction_remove')) {
        likedByThisVu.set(thread.id, false);
      }
    } else if (track(authed(baseUrl, session, (auth) => addReaction(baseUrl, auth, thread.id, DISCARD)), 'reaction_add')) {
      likedByThisVu.set(thread.id, true);
    }
  },

  /** J6 — FTS search with words both seeders actually wrote. */
  J6(baseUrl, data, session, pick) {
    if (REAL_SPA) {
      spa.search(baseUrl, data, session, pick);
      return;
    }
    track(searchThreads(baseUrl, pick.word(), readOpts(session, DISCARD)), 'search');
  },

  /** J7 — profile: stats + the user's threads (the SPA's /u/[userId] core calls, both at mount → parallel). */
  J7(baseUrl, data, session, pick) {
    if (REAL_SPA) {
      spa.profile(baseUrl, data, session, pick);
      return;
    }
    const { thread } = pick.thread(data);
    const [stats, threads] = apiBatch([
      batchGet(baseUrl, spaPaths.userStats(thread.ownerId), 'user_stats', readOpts(session, DISCARD)),
      batchGet(baseUrl, spaPaths.userThreads(thread.ownerId), 'user_threads', readOpts(session, DISCARD)),
    ]);
    track(stats, 'user_stats');
    track(threads, 'user_threads');
  },

  /** J9 — direct message to a random seeded friend: open-or-get that conversation once per pair, then one send. */
  J9(baseUrl, _data, session, pick) {
    const peers = session.peers || [];
    if (peers.length === 0) {
      return;
    }
    const peer = peers[Math.floor(Math.random() * peers.length)];
    const key = `${session.userId}:${peer}`;
    let conversationId = conversationByPair.get(key);
    if (!conversationId) {
      const opened = authed(baseUrl, session, (auth) => openDirectConversation(baseUrl, auth, peer));
      if (!track(opened, 'dm_open', [200, 201])) {
        return;
      }
      conversationId = opened.json('conversationId');
      conversationByPair.set(key, conversationId);
    }
    const body = pick.commentBody();
    track(authed(baseUrl, session, (auth) => sendMessage(baseUrl, auth, conversationId, body, DISCARD)), 'dm_send', [200, 201]);
  },

  /** nav — list spaces (same navigation call on both sides; replaces the A-only tag autocomplete). */
  nav(baseUrl, data, session) {
    if (REAL_SPA) {
      spa.nav(baseUrl, data, session);
      return;
    }
    track(listCategories(baseUrl, readOpts(session, DISCARD)), 'categories');
  },
};

/**
 * J8 — WS hold: mint a single-use ticket, connect, subscribe to a public category, hold holdMs, close.
 * counters = { subscribed, notifications, errors, probesSent, probesReceived, probesLost, probesUnresolved,
 * receipt } (k6 metrics owned by main.js). probe = { intervalMs, timeoutMs } or null.
 * Receipt probe: every intervalMs the holding VU writes a comment into a thread of ITS subscribed category and
 * measures the time from the write request's start until the hub pushes the `comment created` envelope with that
 * comment id back on this socket. Probes stop timeoutMs before the hold ends; a probe unanswered after timeoutMs
 * counts as lost. Returns the ws response (null when the ticket could not be minted).
 */
export function wsHold(baseUrl, data, session, holdMs, counters, pick, probe = null) {
  const ticketRes = authed(baseUrl, session, (auth) => realtimeTicket(baseUrl, auth));
  if (!track(ticketRes, 'ticket')) {
    return null;
  }
  const ticket = ticketRes.json('ticket');
  const space = pick.space(data);
  const wsUrl = baseUrl.replace(/^http/, 'ws');
  const pending = new Map(); // commentId → t0 (request start)
  const early = new Map(); // push seen before the write's response was parsed (never on A, kept symmetric with B)
  const startedAt = Date.now();

  const resolve = (id, receivedAt) => {
    const t0 = pending.get(id);
    if (t0 === undefined) {
      early.set(id, receivedAt);
      return;
    }
    pending.delete(id);
    settle(receivedAt - t0);
  };
  // Pending probes stay until the hold ends: a push that arrives after the timeout is LATE (delivered, slow —
  // A's durable outbox replays after a fault), a probe never answered by the end of the hold is LOST. Only the
  // early-frame cache is pruned here.
  const expire = (now) => {
    for (const [id, at] of early) {
      if (now - at > probe.timeoutMs) {
        early.delete(id);
      }
    }
  };
  const settle = (latency) => {
    counters.receipt.add(latency);
    if (latency > probe.timeoutMs) {
      counters.probesLate.add(1);
    } else {
      counters.probesReceived.add(1);
    }
  };

  return ws.connect(`${wsUrl}/api/realtime/ws?ticket=${encodeURIComponent(ticket)}`, { tags: { endpoint: 'ws' } }, (socket) => {
    socket.on('open', () => {
      socket.send(JSON.stringify({ action: 'subscribe', view: 'category', id: space.key }));
    });
    socket.on('message', (raw) => {
      const msg = JSON.parse(raw);
      if (msg.type === 'subscribed') {
        counters.subscribed.add(1);
        if (probe && probe.intervalMs > 0) {
          socket.setInterval(() => {
            const now = Date.now();
            expire(now);
            if (now - startedAt + probe.timeoutMs > holdMs) {
              return; // too close to the end of the hold to be resolvable either way
            }
            const threads = data.threadsBySpace[space.key] || [];
            if (threads.length === 0) {
              return;
            }
            const thread = threads[Math.floor(Math.random() * threads.length)];
            const t0 = Date.now();
            const res = authed(baseUrl, session, (auth) =>
              createComment(baseUrl, auth, thread.id, `probe ${t0} ${pick.commentBody()}`, { endpoint: 'probe_comment' }));
            if (track(res, 'probe_comment', [200, 201])) {
              const id = res.json('commentId');
              counters.probesSent.add(1);
              const seen = early.get(id);
              if (seen !== undefined) {
                early.delete(id);
                settle(seen - t0);
              } else {
                pending.set(id, t0);
              }
            } else {
              counters.errors.add(1);
            }
          }, probe.intervalMs);
        }
      } else if (msg.type === 'error') {
        counters.errors.add(1);
      } else if (msg.entity) {
        counters.notifications.add(1); // a change notification pushed for the subscribed category
        if (probe && msg.entity === 'comment' && msg.type === 'created') {
          resolve(msg.id, Date.now());
        }
      }
    });
    socket.on('error', () => counters.errors.add(1));
    socket.on('close', () => {
      const now = Date.now();
      if (now - startedAt < holdMs - 2000) {
        counters.earlyClose.add(1); // the server/hub ended the hold, not our timer (e.g. a slow-consumer policy)
      }
      if (!probe) {
        return;
      }
      for (const t0 of pending.values()) {
        if (now - t0 > probe.timeoutMs) {
          counters.probesLost.add(1); // never pushed back within the hold
        } else {
          counters.probesUnresolved.add(1); // sent too close to the end to tell
        }
      }
    });
    socket.setTimeout(() => socket.close(), holdMs);
  });
}
