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

import encoding from 'k6/encoding';
import { sleep } from 'k6';
import { Counter } from 'k6/metrics';
import ws from 'k6/ws';

import {
  addReaction, attachFile, commentTree, commitUpload, createComment, createThread, initiateUpload, listCategories,
  login, openDirectConversation, reactionBatch, realtimeTicket, removeReaction, searchThreads, sendMessage,
  threadDetail, threadFeed, uploadToPresignedUrl, userStats, userThreads,
} from '../lib/api.js';
import { SEARCH_WORDS as SHARED_WORDS, makePng, randomTagSlugs, threadBody, threadTitle } from '../lib/assets.js';
import { track } from '../lib/track.js';

export const NAME = 'a';
export const DEFAULT_BASE_URL = 'https://forum.local';
export const SEARCH_WORDS = SHARED_WORDS; // the word bank BOTH seeders draw from (identical list on B)

const AUTH_READS = __ENV.AUTH_READS === '1' || __ENV.AUTH_READS === 'true';
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
  // J9 peer: the next pool member (an active, ordinary account; A's privacy default = everyone may message).
  for (let i = 0; i < sessions.length; i++) {
    sessions[i].peerUserId = sessions[(i + 1) % sessions.length].userId;
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
  return { sessions, spaces, threadsBySpace, devMode, authReads: AUTH_READS };
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

// J9 conversation per VU: opened on the first DM (get-or-create, 200), reused afterwards — like the SPA's open
// chat dock. Keyed by the session user so a re-login never crosses conversations.
const conversationByUser = new Map();

export const journeys = {
  /** J1 — space feed: page 1 + reaction batch over its cards (the SPA's real render); 30% follow the cursor. */
  J1(baseUrl, data, session, pick) {
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
    const { thread } = pick.thread(data);
    track(threadDetail(baseUrl, thread.id, readOpts(session, DISCARD)), 'thread');
    const tree = commentTree(baseUrl, thread.id, readOpts(session));
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
   */
  J4(baseUrl, data, session, pick) {
    const space = pick.space(data);
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
      createThread(baseUrl, auth, space.key, threadTitle(Math.random), body, randomTagSlugs(Math.random)));
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
  J6(baseUrl, _data, session, pick) {
    track(searchThreads(baseUrl, pick.word(), readOpts(session, DISCARD)), 'search');
  },

  /** J7 — profile: stats + the user's threads (the SPA's /u/[userId] core calls). */
  J7(baseUrl, data, session, pick) {
    const { thread } = pick.thread(data);
    track(userStats(baseUrl, thread.ownerId, readOpts(session, DISCARD)), 'user_stats');
    track(userThreads(baseUrl, thread.ownerId, readOpts(session, DISCARD)), 'user_threads');
  },

  /** J9 — direct message to the VU's peer: open-or-get the conversation once, then one send per journey. */
  J9(baseUrl, _data, session, pick) {
    if (!session.peerUserId) {
      return;
    }
    let conversationId = conversationByUser.get(session.userId);
    if (!conversationId) {
      const opened = authed(baseUrl, session, (auth) => openDirectConversation(baseUrl, auth, session.peerUserId));
      if (!track(opened, 'dm_open', [200, 201])) {
        return;
      }
      conversationId = opened.json('conversationId');
      conversationByUser.set(session.userId, conversationId);
    }
    const body = pick.commentBody();
    track(authed(baseUrl, session, (auth) => sendMessage(baseUrl, auth, conversationId, body, DISCARD)), 'dm_send', [200, 201]);
  },

  /** nav — list spaces (same navigation call on both sides; replaces the A-only tag autocomplete). */
  nav(baseUrl, _data, session) {
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
            if (res.status === 201 || res.status === 200) {
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
      if (!probe) {
        return;
      }
      const now = Date.now();
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
