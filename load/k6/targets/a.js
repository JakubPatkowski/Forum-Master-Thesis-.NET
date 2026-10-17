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

import { sleep } from 'k6';
import ws from 'k6/ws';

import {
  addReaction, commentTree, commitUpload, createComment, createThread, initiateUpload, listCategories,
  login, reactionBatch, realtimeTicket, removeReaction, searchThreads, threadDetail, threadFeed,
  uploadToPresignedUrl, userStats, userThreads,
} from '../lib/api.js';
import { PNG_1X1, PNG_1X1_BYTES, SEARCH_WORDS as SHARED_WORDS, randomTagSlugs, threadBody, threadTitle } from '../lib/assets.js';
import { track } from '../lib/track.js';

export const NAME = 'a';
export const DEFAULT_BASE_URL = 'https://forum.local';
export const SEARCH_WORDS = SHARED_WORDS; // the word bank BOTH seeders draw from (identical list on B)

// Setup-time calls (login pool, pool discovery) are tagged with this endpoint so they never land in a journey's
// endpoint row of the summary table.
const SETUP = { endpoint: 'setup' };
const DISCARD = { discard: true };
const FEED_PAGE = 50; // GetThreadFeedQueryHandler.MaxLimit — the crawl's page size (setup only)
const CRAWL_MAX_PAGES = 200; // safety stop (the Benchmark seed needs ≤ 8 pages per category)

// Endpoint tag values (informational per-endpoint thresholds materialize them in the summary).
export const ENDPOINTS = [
  'login', 'categories', 'feed', 'thread', 'comments', 'reactions_batch', 'search',
  'comment_create', 'thread_create', 'reaction_add', 'reaction_remove', 'file_initiate', 'file_put',
  'file_commit', 'user_stats', 'user_threads', 'ticket', 'setup',
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

/**
 * Runs once (k6 setup): staggered logins + anonymous discovery of the public categories and of EVERY thread in
 * them (keyset crawl to the end, feed order: pinned first, then newest). Returns the target-neutral shape
 * { sessions, spaces, threadsBySpace, devMode } — main.js draws thread journeys uniformly from the whole public
 * corpus and ranks spaces by their crawled size (see its selection-helper comment).
 */
export function setup(baseUrl, poolSize, loginStaggerMs) {
  const probe = login(baseUrl, `bench_user_${String(BENCH_USER_BASE + 1).padStart(4, '0')}@bench.local`, BENCH_PASSWORD);
  if (probe.status === 429) {
    throw new Error('Auth rate limiter hit on the FIRST login — raise RateLimiting__Auth__PermitLimit (bench-run.sh does) and retry.');
  }
  const devMode = probe.token === null;
  if (devMode) {
    console.warn('Benchmark users not found — falling back to the Development seed (alice/bob/charlie). Seed the Benchmark profile for measured runs.');
  }

  const sessions = devMode ? [] : [{ auth: probe.token, userIndex: BENCH_USER_BASE }];
  const wanted = devMode ? DEV_USERS.length : poolSize;
  for (let i = sessions.length; i < wanted; i++) {
    const email = devMode
      ? `${DEV_USERS[i]}@dev.local`
      : `bench_user_${String(BENCH_USER_BASE + 1 + i).padStart(4, '0')}@bench.local`;
    const res = login(baseUrl, email, devMode ? DEV_PASSWORD : BENCH_PASSWORD);
    if (res.status === 429) {
      throw new Error(`Auth rate limiter hit after ${sessions.length} logins — raise RateLimiting__Auth__PermitLimit (bench-run.sh does) and retry.`);
    }
    if (res.token) {
      sessions.push({ auth: res.token, userIndex: BENCH_USER_BASE + i });
    }
    sleep(loginStaggerMs / 1000);
  }
  if (sessions.length === 0) {
    throw new Error('No logins succeeded — is the database seeded? (make seed ARGS="--benchmark --cluster")');
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
  return { sessions, spaces, threadsBySpace, devMode };
}

function batchFeedIds(baseUrl, body) {
  const ids = body.items.map((t) => t.id);
  if (ids.length > 0) {
    track(reactionBatch(baseUrl, 'thread', ids, DISCARD), 'reactions_batch');
  }
}

// J5 like state, per VU (each k6 VU has its own JS runtime and a fixed session — main.js vuSession). The A
// endpoints are idempotent, so a blind 50/50 PUT/DELETE made ~half of all J5 calls write-free no-ops on A while
// B's vote toggle ALWAYS writes; tracking the state makes every J5 a real state change on both sides. The rare
// seeded like by the same user (≈0.2% of pairs) only turns that first PUT into a no-op.
const likedByThisVu = new Map();

export const journeys = {
  /** J1 — space feed: page 1 + reaction batch over its cards (the SPA's real render); 30% follow the cursor. */
  J1(baseUrl, data, _session, pick) {
    const space = pick.space(data);
    const first = threadFeed(baseUrl, space.key, null, 20);
    if (!track(first, 'feed')) {
      return;
    }
    const body = first.json();
    batchFeedIds(baseUrl, body);
    if (Math.random() < 0.3 && body.hasMore && body.nextCursor) {
      const second = threadFeed(baseUrl, space.key, body.nextCursor, 20);
      if (track(second, 'feed')) {
        batchFeedIds(baseUrl, second.json());
      }
    }
  },

  /** J2 — thread open: detail + comment tree + reaction batch over the comments (SPA: sorted ids, ≤100). */
  J2(baseUrl, data, _session, pick) {
    const { thread } = pick.thread(data);
    track(threadDetail(baseUrl, thread.id, DISCARD), 'thread');
    const tree = commentTree(baseUrl, thread.id);
    let batchIds = [thread.id];
    let batchType = 'thread';
    if (track(tree, 'comments')) {
      const comments = tree.json();
      if (comments.length > 0) {
        batchIds = comments.map((c) => c.id).sort().slice(0, 100); // = lib/hooks/use-reactions.ts useReactionBatch
        batchType = 'comment';
      }
    }
    track(reactionBatch(baseUrl, batchType, batchIds, DISCARD), 'reactions_batch');
  },

  /** J3 — top-level comment (replies risk the depth-5 422 on seeded depth-4 chains). */
  J3(baseUrl, data, session, pick) {
    const { thread } = pick.thread(data);
    track(createComment(baseUrl, session.auth, thread.id, pick.commentBody(), DISCARD), 'comment_create', [200, 201]);
  },

  /** J4 — thread with 1–3 existing seeded tags (weight 0 in mix v0 — B adapter lacks J4 until P1). */
  J4(baseUrl, data, session, pick) {
    const space = pick.space(data);
    track(createThread(baseUrl, session.auth, space.key, threadTitle(Math.random), threadBody(Math.random), randomTagSlugs(Math.random)),
      'thread_create', [200, 201]);
  },

  /** J4 image arm — ADR 0008 golden path (weight 0 in mix v0). */
  J4upload(baseUrl, _data, session) {
    const initiate = initiateUpload(baseUrl, session.auth, 'image/png', PNG_1X1_BYTES);
    if (track(initiate, 'file_initiate', [200, 201])) {
      const uploadUrl = initiate.json('uploadUrl');
      const fileId = initiate.json('fileId');
      if (track(uploadToPresignedUrl(uploadUrl, PNG_1X1, 'image/png'), 'file_put')) {
        track(commitUpload(baseUrl, session.auth, fileId), 'file_commit');
      }
    }
  },

  /** J5 — reaction toggle: always the state-changing direction (like the SPA button / B's vote toggle). */
  J5(baseUrl, data, session, pick) {
    const { thread } = pick.thread(data);
    if (likedByThisVu.get(thread.id)) {
      if (track(removeReaction(baseUrl, session.auth, thread.id, DISCARD), 'reaction_remove')) {
        likedByThisVu.set(thread.id, false);
      }
    } else if (track(addReaction(baseUrl, session.auth, thread.id, DISCARD), 'reaction_add')) {
      likedByThisVu.set(thread.id, true);
    }
  },

  /** J6 — FTS search with words both seeders actually wrote. */
  J6(baseUrl, _data, _session, pick) {
    track(searchThreads(baseUrl, pick.word(), DISCARD), 'search');
  },

  /** J7 — profile: stats + the user's threads (the SPA's /u/[userId] core calls). */
  J7(baseUrl, data, _session, pick) {
    const { thread } = pick.thread(data);
    track(userStats(baseUrl, thread.ownerId, DISCARD), 'user_stats');
    track(userThreads(baseUrl, thread.ownerId, DISCARD), 'user_threads');
  },

  /** nav — list spaces (same navigation call on both sides; replaces the A-only tag autocomplete). */
  nav(baseUrl) {
    track(listCategories(baseUrl, DISCARD), 'categories');
  },
};

/**
 * J8 — WS hold: mint a single-use ticket, connect, subscribe to a public category, hold holdMs, close.
 * counters = { subscribed, notifications, errors } (k6 Counters owned by main.js). Returns the ws response
 * (null when the ticket could not be minted).
 */
export function wsHold(baseUrl, data, session, holdMs, counters, pick) {
  const ticketRes = realtimeTicket(baseUrl, session.auth);
  if (!track(ticketRes, 'ticket')) {
    return null;
  }
  const ticket = ticketRes.json('ticket');
  const space = pick.space(data);
  const wsUrl = baseUrl.replace(/^http/, 'ws');
  return ws.connect(`${wsUrl}/api/realtime/ws?ticket=${encodeURIComponent(ticket)}`, { tags: { endpoint: 'ws' } }, (socket) => {
    socket.on('open', () => {
      socket.send(JSON.stringify({ action: 'subscribe', view: 'category', id: space.key }));
    });
    socket.on('message', (raw) => {
      const msg = JSON.parse(raw);
      if (msg.type === 'subscribed') {
        counters.subscribed.add(1);
      } else if (msg.type === 'error') {
        counters.errors.add(1);
      } else if (msg.entity) {
        counters.notifications.add(1); // a change notification pushed for the subscribed category
      }
    });
    socket.on('error', () => counters.errors.add(1));
    socket.setTimeout(() => socket.close(), holdMs);
  });
}
