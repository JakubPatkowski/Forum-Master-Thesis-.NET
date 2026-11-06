// Target adapter: Architecture B (gomx — Go + Templ/HTMX SSR monolith). Interface: targets/README.md.
//
// Wire facts this adapter encodes (verified against gomx master @ 8b23bc1, 2026-10-16, Hubert's alignment
// branch feature/master-thesis @ f03d46a + the 2026-10-17 alignment edits, and re-checked against master @ e4b1d8d
// on 2026-10-18 — see docs/architecture/AB-ALIGNMENT-INSTRUCTIONS-B.md §4, the contract both sides keep in sync):
//   login     POST /login form username+password → 303 + Set-Cookie forum_session (422 on failure);
//             bench seed = benchuser0001..0800 / DemoPassword1, demo seed = gomx|grace|ada|… / DemoPassword1
//   spaces    GET /communities (anonymous → public only), cards link href="/c/{slug}"
//   J1        GET /c/{slug} (full page, 20 <article id="post-N">); next page = the hx-get of #feed-sentinel
//             (/c/{slug}/feed?page=N&cursor=…) — `page` alone is cosmetic, the cursor selects the page
//   J2        GET /thread/{id}        J6  GET /search?q=        J7  GET /u/{numericId}
//   J3        POST /posts/{id}/comments form body → 303 Location /thread/{id}#comment-{new}
//   J4        POST /uploads multipart `file` → 202 {upload_id} (River + MinIO staging in k8s; 201 {id} without)
//             → GET /uploads/{upload_id}/status every 1 s (the browser's cadence) until {status:"completed",
//             result:{id,url}} → POST /posts form community+title+body (the browser embeds `![](/uploads/<id>)`
//             in the body, no attachments field) → 303 /thread/{id} (303 / on rejection); CreatePost needs an
//             ACTIVE membership — the bench seed pre-joins every user to 3 public spaces (postableSlug)
//   J5        POST /posts/{id}/vote form dir=up + HX-Request: true → 200 fragment (server-side toggle)
//   J9        POST /messages/{peerId} form body → ALWAYS 303 /messages/{peerId} (also on rejection) — sending
//             requires friendship; peers are discovered per session in setup from GET /messages (href="/messages/N",
//             the bench seed's friend ring: ±1, ±7, ±40). The write is verified by the dataset delta (run.json).
//   CSRF      Origin-vs-Host; a missing Origin header is allowed (we send none)
//   WS        GET /ws with the session cookie; no subscribe protocol — every socket gets every broadcast; a new
//             comment arrives as {"type":"comment","postId":N,"html":"…<article id=\"comment-M\"…"} — the id lives
//             only in the HTML; the frame is published to Redis BEFORE the 303 is written
// Every request goes out with a FRESH, empty http.CookieJar: k6's per-VU jar would otherwise replay the
// last login's forum_session (and gomx_locale) on the "anonymous" read journeys and OVERRIDE an explicit
// Cookie header on writes (verified on k6 v2.1.0 — `jar: null` does NOT disable the VU jar).
// Harness v1 (2026-10-17): shared search words (B-09 ported A's word bank), the thread pool is the WHOLE public
// corpus (sentinel crawl to the end — same rule as targets/a.js), unparsed bodies are discarded, every request
// asks for gzip (lib/wire.js; B's in-process gzip is off in the bench overlay, the ingress compresses both).
// Harness h4 (2026-10-18): J4 (+image) and J9 as above, AUTH_READS=1 sends the session cookie on reads (signed-in
// browsing, D3 — B then pays per-card vote/membership lookups), J8 holds send receipt probes (own comment →
// matching broadcast frame).
// Harness h6 (2026-10-24): rejected writes are visible — J9's handler (thesis-v3-b) redirects a rejected DM to
// /messages/{peer}?error=rejected, a rejected/failed post never lands on /thread/0 (regex requires a positive id), and
// every failed check counts (main.js `checks{scenario:http}` threshold + run.json validity). Receipt-probe caveat: the
// probe POST runs inside the socket's event loop, so a frame B publishes BEFORE writing the 303 is only timestamped
// after the POST returns — B's receipt latency is bounded below by its own comment round-trip (≈ the POST time) and
// is an upper bound of the true push latency (D14).

import { sleep } from 'k6';
import http from 'k6/http';
import ws from 'k6/ws';

import { SEARCH_WORDS as SHARED_WORDS, makePng, threadBody, threadTitle } from '../lib/assets.js';
import { track } from '../lib/track.js';
import { BROWSER_HEADERS, DISCARD } from '../lib/wire.js';

export const NAME = 'b';
export const DEFAULT_BASE_URL = 'https://gomx.local';

// B-09 ported A's word bank verbatim into seedbench.go (every body starts with `seeded` too), so both targets
// search the SAME words. ILIKE is case-insensitive on B; the words are lowercase anyway.
export const SEARCH_WORDS = SHARED_WORDS;

const AUTH_READS = __ENV.AUTH_READS === '1' || __ENV.AUTH_READS === 'true';
// BOOSTED (HTMX hx-boost navigation): When enabled (default: true for gomx), in-app page navigations
// (J1, J2, J6, J7, nav) send 'HX-Request: true' and 'HX-Boosted: true'. Gomx responds with BoostedPage
// (only <title> and <main id="page"> content), skipping the persistent outer shell (header, sidebar, modals)
// and bypassing the withShell database queries (PostableCommunities, TotalUnreadDMs, UnreadCount).
// This reflects idiomatic HTMX user navigation in the browser and matches Target A's mounted-shell assumption.
// Set BOOSTED=0 to force cold multi-page document reloads.
export const BOOSTED = __ENV.BOOSTED !== '0' && __ENV.BOOSTED !== 'false';
const J4_IMAGE_PX = Number(__ENV.J4_IMAGE_PX || 256);
const J4_POLL_INTERVAL_S = Number(__ENV.J4_POLL_INTERVAL_S || 0.15); // upload.ts adaptive polling starts at 150 ms (D12 fix)
const J4_POLL_MAX = Number(__ENV.J4_POLL_MAX || 40);

export const ENDPOINTS = ['login', 'communities', 'space_page', 'space_feed', 'thread', 'search', 'profile',
  'comment_create', 'vote', 'upload', 'upload_poll', 'thread_create', 'dm_send', 'probe_comment', 'ws', 'setup'];

export function hosts(ingressIp) {
  return ingressIp ? { 'gomx.local': ingressIp } : {};
}

// Bench seed identities (seedbench.go benchAccounts). Same ordinary band as A (0034+).
const USER_BASE = 33;
const PASSWORD = 'DemoPassword1';
const DEMO_USERS = ['gomx', 'grace', 'ada', 'linus', 'edsger', 'katherine', 'alan', 'margaret', 'barbara', 'donald'];
const CRAWL_MAX_PAGES = 200; // safety stop (the bench seed needs ≤ 17 pages of 20 per space)

// Community index → slug (seedbench.go benchCommunities); since B-09 the LAST FOUR (indices 8–11) are private.
// Bench user u (0-based) is an active member of the public spaces (u+off)%12 for off ∈ {0,5,9}, remapped to
// (u+off)%8 when that index is private (seedbench.go), and may POST only there (rules.go CanPostCommunity). The
// off=0 slot is therefore always a public membership — J4 posts there.
const SLUGS = ['general', 'showcase', 'golang', 'design', 'help', 'random',
  'bench7', 'bench8', 'bench9', 'bench10', 'bench11', 'bench12'];
const PUBLIC_SPACES = 8;
export function postableSlug(userIndex) {
  const idx = userIndex % SLUGS.length;
  return SLUGS[idx < PUBLIC_SPACES ? idx : userIndex % PUBLIC_SPACES];
}

/** A throwaway jar per request: nothing replayed in, received cookies discarded. */
const noJar = () => ({ jar: new http.CookieJar() });

/** Anonymous GET by default; under AUTH_READS the VU's session cookie rides along (signed-in page).
 * When boosted is true (and BOOSTED is active), sends HX-Request: true and HX-Boosted: true to simulate
 * in-app HTMX navigation (yielding BoostedPage, skipping the persistent shell and withShell queries). */
function get(baseUrl, path, endpoint, { keepBody = false, session = null, boosted = false } = {}) {
  const auth = session && (AUTH_READS || endpoint === 'setup') ? { Cookie: session.auth } : {};
  const boost = boosted && BOOSTED && endpoint !== 'setup' ? { 'HX-Request': 'true', 'HX-Boosted': 'true' } : {};
  return http.get(`${baseUrl}${path}`, {
    ...noJar(), ...(keepBody ? {} : DISCARD), redirects: 0, tags: { endpoint }, headers: { ...BROWSER_HEADERS, ...auth, ...boost },
  });
}

function postForm(baseUrl, path, form, endpoint, session, headers = {}, extra = {}) {
  return http.post(`${baseUrl}${path}`, form, {
    ...noJar(), ...DISCARD, ...extra, redirects: 0, tags: { endpoint },
    headers: { ...BROWSER_HEADERS, Cookie: session.auth, ...headers },
  });
}

const unescapeHtml = (s) => s.replace(/&amp;/g, '&');
const SENTINEL = /id="feed-sentinel"[^>]*hx-get="([^"]+)"/;
const location = (res) => (res.headers && (res.headers.Location || res.headers.location)) || '';

function login(baseUrl, username) {
  const res = http.post(`${baseUrl}/login`, { username, password: PASSWORD },
    { ...noJar(), redirects: 0, tags: { endpoint: 'login' }, headers: { ...BROWSER_HEADERS } });
  const cookie = res.cookies && res.cookies.forum_session;
  if (res.status !== 303 || !cookie || cookie.length === 0 || !cookie[0].value) {
    return { auth: null, status: res.status };
  }
  return { auth: `forum_session=${cookie[0].value}`, status: res.status };
}

/** Public space slugs from /communities (anonymous). `new` is the create-community link, not a slug. */
function discoverSpaces(baseUrl) {
  const res = get(baseUrl, '/communities', 'setup', { keepBody: true });
  if (res.status !== 200) {
    throw new Error(`GET /communities failed (${res.status}) — cannot build the traffic pools.`);
  }
  const slugs = new Set();
  for (const m of res.body.matchAll(/href="\/c\/([a-z0-9-]+)"/g)) {
    if (m[1] !== 'new') {
      slugs.add(m[1]);
    }
  }
  return [...slugs].map((slug) => ({ key: slug, slug }));
}

/** Thread ids + author ids from feed HTML: <article id="post-N" …> … href="/u/ID" (post.templ). */
function parseThreads(html, into, seen) {
  for (const m of html.matchAll(/<article id="post-(\d+)"[\s\S]*?href="\/u\/(\d+)"/g)) {
    if (!seen.has(m[1])) {
      seen.add(m[1]);
      into.push({ id: m[1], ownerId: m[2] });
    }
  }
}

/** J9 peers: the friends listed on the session's own /messages inbox (href="/messages/<id>"). */
function discoverPeers(baseUrl, session) {
  const res = get(baseUrl, '/messages', 'setup', { keepBody: true, session });
  if (res.status !== 200) {
    return [];
  }
  const peers = new Set();
  for (const m of res.body.matchAll(/\/messages\/(\d+)/g)) {
    peers.add(m[1]);
  }
  return [...peers];
}

export function setup(baseUrl, poolSize, loginStaggerMs) {
  const probe = login(baseUrl, `benchuser${String(USER_BASE + 1).padStart(4, '0')}`);
  if (probe.status === 429) {
    throw new Error('B auth limiter active — GOMX_AUTH_RATELIMIT=0 is missing in the bench overlay.');
  }
  const devMode = probe.auth === null;
  if (devMode) {
    console.warn('Bench users not found on B — falling back to the demo seed (gomx/grace/ada/…). Deploy GOMX_SEED=bench for measured runs.');
  }

  const sessions = devMode ? [] : [{ auth: probe.auth, userIndex: USER_BASE }];
  const wanted = devMode ? DEMO_USERS.length : poolSize;
  for (let i = sessions.length; i < wanted; i++) {
    const username = devMode ? DEMO_USERS[i] : `benchuser${String(USER_BASE + 1 + i).padStart(4, '0')}`;
    const res = login(baseUrl, username);
    if (res.status === 429) {
      throw new Error(`B auth limiter hit after ${sessions.length} logins — set GOMX_AUTH_RATELIMIT=0.`);
    }
    if (res.auth) {
      sessions.push({ auth: res.auth, userIndex: USER_BASE + i });
    }
    sleep(loginStaggerMs / 1000);
  }
  if (sessions.length === 0) {
    throw new Error('No B logins succeeded — is the server seeded? (GOMX_SEED=bench, /stats posts=1600)');
  }
  // J9 peers per session (the seed's friend ring); one inbox page per pool member, setup-tagged.
  let peerless = 0;
  for (const session of sessions) {
    session.peers = discoverPeers(baseUrl, session);
    if (session.peers.length === 0) {
      peerless++;
    }
  }
  if (peerless > 0) {
    console.warn(`B: ${peerless} of ${sessions.length} sessions have no friends on /messages — their J9 journeys will be skipped.`);
  }

  const spaces = discoverSpaces(baseUrl);
  if (spaces.length === 0) {
    throw new Error('No public communities found on /communities — seed the database first.');
  }

  // Thread pool: the WHOLE public corpus — the space page plus every cursor page behind the sentinel, in feed
  // order (pinned first, then newest). Same rule as targets/a.js (see the comment there for why a depth-limited
  // "newest N" pool was unfair between the two seeds).
  const threadsBySpace = {};
  for (const space of spaces) {
    const threads = [];
    const seen = new Set();
    let page = get(baseUrl, `/c/${space.slug}`, 'setup', { keepBody: true });
    if (page.status !== 200) {
      console.warn(`B: /c/${space.slug} returned ${page.status} anonymously — dropped from the pool (private or missing).`);
      continue;
    }
    for (let n = 0; n < CRAWL_MAX_PAGES && page.status === 200; n++) {
      const before = threads.length;
      parseThreads(page.body, threads, seen);
      const m = page.body.match(SENTINEL);
      if (!m || threads.length === before) {
        break; // last page: no sentinel (or nothing new — never loop on a stale cursor)
      }
      page = get(baseUrl, unescapeHtml(m[1]), 'setup', { keepBody: true });
    }
    threadsBySpace[space.key] = threads;
  }
  const publicSpaces = spaces.filter((s) => s.key in threadsBySpace);
  return { sessions, spaces: publicSpaces, threadsBySpace, devMode, authReads: AUTH_READS };
}

/** Uploads a PNG through B's server-side ingest; returns the blob id or null (every step tracked). */
function uploadImage(baseUrl, session, png) {
  const res = http.post(`${baseUrl}/uploads`, { file: http.file(png.bytes, 'photo.png', 'image/png') }, {
    ...noJar(), redirects: 0, tags: { endpoint: 'upload' }, headers: { ...BROWSER_HEADERS, Cookie: session.auth },
  });
  if (!track(res, 'upload', [201, 202])) {
    return null;
  }
  if (res.status === 201) {
    return res.json('id'); // synchronous fallback (no River/S3 staging) — not the k8s path
  }
  const uploadId = res.json('upload_id');
  let pollInterval = J4_POLL_INTERVAL_S;
  for (let i = 0; i < J4_POLL_MAX; i++) {
    sleep(pollInterval);
    const poll = http.get(`${baseUrl}/uploads/${uploadId}/status`, {
      ...noJar(), redirects: 0, tags: { endpoint: 'upload_poll' }, headers: { ...BROWSER_HEADERS, Cookie: session.auth },
    });
    if (!track(poll, 'upload_poll')) {
      return null;
    }
    const status = poll.json('status');
    if (status === 'completed') {
      return poll.json('result.id');
    }
    if (status === 'failed') {
      track({ status: 599 }, 'upload_poll'); // processing failed server-side — recorded as a failed check
      return null;
    }
    pollInterval = Math.min(pollInterval * 1.5, 1.0);
  }
  track({ status: 598 }, 'upload_poll'); // never completed within the poll budget
  return null;
}

export const journeys = {
  /** J1 — space page (20 cards); 30% follow the sentinel's cursor to page 2 (body kept only then). */
  J1(baseUrl, data, session, pick) {
    const space = pick.space(data);
    const follow = Math.random() < 0.3;
    const first = get(baseUrl, `/c/${space.slug}`, 'space_page', { keepBody: follow, session, boosted: true });
    if (track(first, 'space_page') && follow) {
      const m = first.body.match(SENTINEL);
      if (m) {
        track(get(baseUrl, unescapeHtml(m[1]), 'space_feed', { session, boosted: true }), 'space_feed');
      }
    }
  },

  /** J2 — thread page (whole comment tree server-rendered). */
  J2(baseUrl, data, session, pick) {
    const { thread } = pick.thread(data);
    track(get(baseUrl, `/thread/${thread.id}`, 'thread', { session, boosted: true }), 'thread');
  },

  /** J3 — top-level comment; 303 whose Location carries the new comment id. */
  J3(baseUrl, data, session, pick) {
    const { thread } = pick.thread(data);
    const res = postForm(baseUrl, `/posts/${thread.id}/comments`, { body: pick.commentBody() }, 'comment_create', session);
    // A rejected comment is ALSO a 303 (back to the thread, no #comment- fragment) — count it as a failure.
    const ok = res.status === 303 && /#comment-\d+$/.test(location(res));
    track({ status: ok ? 303 : (res.status === 303 ? 599 : res.status) }, 'comment_create', [303]);
  },

  /**
   * J4 — thread with a photo through B's ingest: multipart upload (202) → status polls at the browser's 1 s
   * cadence until the River worker has staged the blob → POST /posts with the image embedded in the body (what the
   * composer's data-md-insert does). The journey's duration includes the poll waits — exactly what the user sees.
   */
  J4(baseUrl, _data, session) {
    const png = makePng(J4_IMAGE_PX, J4_IMAGE_PX, Math.random);
    const blobId = uploadImage(baseUrl, session, png);
    if (!blobId) {
      return;
    }
    const body = `${threadBody(Math.random)}\n\n![](/uploads/${blobId})\n`;
    const res = postForm(baseUrl, '/posts', {
      community: postableSlug(session.userIndex), title: threadTitle(Math.random), body,
    }, 'thread_create', session);
    // A rejected post redirects to `/`; a store error used to redirect to `/thread/0` (fixed in thesis-v3-b, and the
    // regex requires a positive id anyway) — both are failures of the journey, recorded as a failed check.
    const ok = res.status === 303 && /\/thread\/[1-9]\d*$/.test(location(res));
    track({ status: ok ? 303 : (res.status === 303 ? 599 : res.status) }, 'thread_create', [303]);
  },

  /** J5 — vote toggle (HX-Request → 200 fragment, no redirect); the server flips state on every call. */
  J5(baseUrl, data, session, pick) {
    const { thread } = pick.thread(data);
    track(postForm(baseUrl, `/posts/${thread.id}/vote`, { dir: 'up' }, 'vote', session, { 'HX-Request': 'true' }), 'vote');
  },

  /** J6 — search page. */
  J6(baseUrl, _data, session, pick) {
    track(get(baseUrl, `/search?q=${encodeURIComponent(pick.word())}`, 'search', { session, boosted: true }), 'search');
  },

  /** J7 — public profile of a thread author. */
  J7(baseUrl, data, session, pick) {
    const { thread } = pick.thread(data);
    track(get(baseUrl, `/u/${thread.ownerId}`, 'profile', { session, boosted: true }), 'profile');
  },

  /**
   * J9 — direct message to one of the session's seeded friends. Accepted → 303 /messages/{peer}; rejected (not
   * friends, empty body, unknown peer) → 303 /messages/{peer}?error=rejected (thesis-v3-b) — the marker turns the
   * rejection into a failed check; a redirect anywhere else (/login?next=…) fails the same way.
   */
  J9(baseUrl, _data, session, pick) {
    const peers = session.peers || [];
    if (peers.length === 0) {
      return;
    }
    const peer = peers[Math.floor(Math.random() * peers.length)];
    const res = postForm(baseUrl, `/messages/${peer}`, { body: pick.commentBody() }, 'dm_send', session);
    const ok = res.status === 303 && location(res).replace(/^https?:\/\/[^/]+/, '') === `/messages/${peer}`;
    track({ status: ok ? 303 : (res.status === 303 ? 599 : res.status) }, 'dm_send', [303]);
  },

  /** nav — list spaces. */
  nav(baseUrl, _data, session) {
    track(get(baseUrl, '/communities', 'communities', { session, boosted: true }), 'communities');
  },
};

/**
 * J8 — WS hold. B has no subscribe protocol: a successful open counts as "subscribed". Receipt probe: every
 * intervalMs the holding VU posts a comment (any public thread — every socket receives every comment) and times
 * the write request's start until the frame carrying `id="comment-<thatId>"` arrives on this socket. B publishes
 * the frame before it writes the 303, so a frame may be delivered to this VU's event loop before the response is
 * parsed — `early` keeps such frames until the id is known.
 */
export function wsHold(baseUrl, data, session, holdMs, counters, pick, probe = null) {
  const wsUrl = baseUrl.replace(/^http/, 'ws');
  const pending = new Map(); // commentId → t0
  const early = new Map(); // commentId → receive time (frame arrived before the 303 was parsed)
  const startedAt = Date.now();

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

  return ws.connect(`${wsUrl}/ws`, { headers: { Cookie: session.auth }, tags: { endpoint: 'ws' } }, (socket) => {
    socket.on('open', () => {
      counters.subscribed.add(1);
      if (probe && probe.intervalMs > 0) {
        socket.setInterval(() => {
          const now = Date.now();
          expire(now);
          if (now - startedAt + probe.timeoutMs > holdMs) {
            return;
          }
          const { thread } = pick.thread(data);
          const t0 = Date.now();
          const res = postForm(baseUrl, `/posts/${thread.id}/comments`, { body: `probe ${t0} ${pick.commentBody()}` },
            'probe_comment', session);
          const m = location(res).match(/#comment-(\d+)$/);
          const accepted = res.status === 303 && Boolean(m);
          track({ status: accepted ? 303 : (res.status === 303 ? 599 : res.status) }, 'probe_comment', [303]);
          if (accepted) {
            const id = m[1];
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
    });
    socket.on('message', (raw) => {
      let msg;
      try {
        msg = JSON.parse(raw);
      } catch {
        return;
      }
      if (msg.type === 'post' || msg.type === 'comment') {
        counters.notifications.add(1);
        if (probe && msg.type === 'comment' && typeof msg.html === 'string') {
          const m = msg.html.match(/id="comment-(\d+)"/);
          if (m) {
            const receivedAt = Date.now();
            const t0 = pending.get(m[1]);
            if (t0 === undefined) {
              early.set(m[1], receivedAt);
            } else {
              pending.delete(m[1]);
              settle(receivedAt - t0);
            }
          }
        }
      }
    });
    socket.on('error', () => counters.errors.add(1));
    socket.on('close', () => {
      const now = Date.now();
      if (now - startedAt < holdMs - 2000) {
        counters.earlyClose.add(1); // the hub closed us (slow-consumer policy 1008, pod gone), not our timer
      }
      if (!probe) {
        return;
      }
      for (const t0 of pending.values()) {
        if (now - t0 > probe.timeoutMs) {
          counters.probesLost.add(1);
        } else {
          counters.probesUnresolved.add(1);
        }
      }
    });
    socket.setTimeout(() => socket.close(), holdMs);
  });
}
