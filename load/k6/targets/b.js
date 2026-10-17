// Target adapter: Architecture B (gomx — Go + Templ/HTMX SSR monolith). Interface: targets/README.md.
//
// Wire facts this adapter encodes (verified against gomx master @ 8b23bc1, 2026-10-16, and Hubert's alignment
// branch feature/master-thesis @ e13f18f + the 2026-10-17 alignment edits — see
// docs/architecture/AB-ALIGNMENT-INSTRUCTIONS-B.md §4, the contract both sides keep in sync):
//   login     POST /login form username+password → 303 + Set-Cookie forum_session (422 on failure);
//             bench seed = benchuser0001..0800 / DemoPassword1, demo seed = gomx|grace|ada|… / DemoPassword1
//   spaces    GET /communities (anonymous → public only), cards link href="/c/{slug}"
//   J1        GET /c/{slug} (full page, 20 <article id="post-N">); next page = the hx-get of #feed-sentinel
//             (/c/{slug}/feed?page=N&cursor=…) — `page` alone is cosmetic, the cursor selects the page
//   J2        GET /thread/{id}        J6  GET /search?q=        J7  GET /u/{numericId}
//   J3        POST /posts/{id}/comments form body → 303 Location /thread/{id}#comment-{new}
//   J5        POST /posts/{id}/vote form dir=up + HX-Request: true → 200 fragment (server-side toggle)
//   CSRF      Origin-vs-Host; a missing Origin header is allowed (we send none)
//   WS        GET /ws with the session cookie; no subscribe protocol — every socket gets every broadcast
// Every request goes out with a FRESH, empty http.CookieJar: k6's per-VU jar would otherwise replay the
// last login's forum_session (and gomx_locale) on the "anonymous" read journeys and OVERRIDE an explicit
// Cookie header on writes (verified on k6 v2.1.0 — `jar: null` does NOT disable the VU jar).
// Harness v1 (2026-10-17): shared search words (B-09 ported A's word bank), the thread pool is the WHOLE public
// corpus (sentinel crawl to the end — same rule as targets/a.js), unparsed bodies are discarded, every request
// asks for gzip (lib/wire.js; B's in-process gzip is off in the bench overlay, the ingress compresses both).

import { sleep } from 'k6';
import http from 'k6/http';
import ws from 'k6/ws';

import { SEARCH_WORDS as SHARED_WORDS } from '../lib/assets.js';
import { track } from '../lib/track.js';
import { BROWSER_HEADERS, DISCARD } from '../lib/wire.js';

export const NAME = 'b';
export const DEFAULT_BASE_URL = 'https://gomx.local';

// B-09 ported A's word bank verbatim into seedbench.go (every body starts with `seeded` too), so both targets
// search the SAME words. ILIKE is case-insensitive on B; the words are lowercase anyway.
export const SEARCH_WORDS = SHARED_WORDS;

export const ENDPOINTS = ['login', 'communities', 'space_page', 'space_feed', 'thread', 'search', 'profile',
  'comment_create', 'vote', 'ws', 'setup'];

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
// off=0 slot is therefore always a public membership. Used by J4 once it lands (P1, weight 0 in mix v1).
const SLUGS = ['general', 'showcase', 'golang', 'design', 'help', 'random',
  'bench7', 'bench8', 'bench9', 'bench10', 'bench11', 'bench12'];
const PUBLIC_SPACES = 8;
export function postableSlug(userIndex) {
  const idx = userIndex % SLUGS.length;
  return SLUGS[idx < PUBLIC_SPACES ? idx : userIndex % PUBLIC_SPACES];
}

/** A throwaway jar per request: nothing replayed in, received cookies discarded. */
const noJar = () => ({ jar: new http.CookieJar() });

function get(baseUrl, path, endpoint, { keepBody = false } = {}) {
  return http.get(`${baseUrl}${path}`, {
    ...noJar(), ...(keepBody ? {} : DISCARD), redirects: 0, tags: { endpoint }, headers: { ...BROWSER_HEADERS },
  });
}

function postForm(baseUrl, path, form, endpoint, session, headers = {}) {
  return http.post(`${baseUrl}${path}`, form, {
    ...noJar(), ...DISCARD, redirects: 0, tags: { endpoint },
    headers: { ...BROWSER_HEADERS, Cookie: session.auth, ...headers },
  });
}

const unescapeHtml = (s) => s.replace(/&amp;/g, '&');
const SENTINEL = /id="feed-sentinel"[^>]*hx-get="([^"]+)"/;

function login(baseUrl, username) {
  const res = http.post(`${baseUrl}/login`, { username, password: PASSWORD },
    { ...noJar(), redirects: 0, tags: { endpoint: 'login' }, headers: { ...BROWSER_HEADERS } });
  const cookie = res.cookies && res.cookies.forum_session;
  if (res.status !== 303 || !cookie || cookie.length === 0) {
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
  return { sessions, spaces: publicSpaces, threadsBySpace, devMode };
}

export const journeys = {
  /** J1 — space page (20 cards); 30% follow the sentinel's cursor to page 2 (body kept only then). */
  J1(baseUrl, data, _session, pick) {
    const space = pick.space(data);
    const follow = Math.random() < 0.3;
    const first = get(baseUrl, `/c/${space.slug}`, 'space_page', { keepBody: follow });
    if (track(first, 'space_page') && follow) {
      const m = first.body.match(SENTINEL);
      if (m) {
        track(get(baseUrl, unescapeHtml(m[1]), 'space_feed'), 'space_feed');
      }
    }
  },

  /** J2 — thread page (whole comment tree server-rendered). */
  J2(baseUrl, data, _session, pick) {
    const { thread } = pick.thread(data);
    track(get(baseUrl, `/thread/${thread.id}`, 'thread'), 'thread');
  },

  /** J3 — top-level comment; 303 whose Location carries the new comment id. */
  J3(baseUrl, data, session, pick) {
    const { thread } = pick.thread(data);
    const res = postForm(baseUrl, `/posts/${thread.id}/comments`, { body: pick.commentBody() }, 'comment_create', session);
    const location = (res.headers && (res.headers.Location || res.headers.location)) || '';
    // A rejected comment is ALSO a 303 (back to the thread, no #comment- fragment) — count it as a failure.
    const ok = res.status === 303 && /#comment-\d+$/.test(location);
    track({ status: ok ? 303 : (res.status === 303 ? 599 : res.status) }, 'comment_create', [303]);
  },

  /** J5 — vote toggle (HX-Request → 200 fragment, no redirect); the server flips state on every call. */
  J5(baseUrl, data, session, pick) {
    const { thread } = pick.thread(data);
    track(postForm(baseUrl, `/posts/${thread.id}/vote`, { dir: 'up' }, 'vote', session, { 'HX-Request': 'true' }), 'vote');
  },

  /** J6 — search page. */
  J6(baseUrl, _data, _session, pick) {
    track(get(baseUrl, `/search?q=${encodeURIComponent(pick.word())}`, 'search'), 'search');
  },

  /** J7 — public profile of a thread author. */
  J7(baseUrl, data, _session, pick) {
    const { thread } = pick.thread(data);
    track(get(baseUrl, `/u/${thread.ownerId}`, 'profile'), 'profile');
  },

  /** nav — list spaces. */
  nav(baseUrl) {
    track(get(baseUrl, '/communities', 'communities'), 'communities');
  },
};

/** J8 — WS hold. B has no subscribe protocol: a successful open counts as "subscribed". */
export function wsHold(baseUrl, _data, session, holdMs, counters) {
  const wsUrl = baseUrl.replace(/^http/, 'ws');
  return ws.connect(`${wsUrl}/ws`, { headers: { Cookie: session.auth }, tags: { endpoint: 'ws' } }, (socket) => {
    socket.on('open', () => counters.subscribed.add(1));
    socket.on('message', (raw) => {
      let msg;
      try {
        msg = JSON.parse(raw);
      } catch {
        return;
      }
      if (msg.type === 'post' || msg.type === 'comment') {
        counters.notifications.add(1);
      }
    });
    socket.on('error', () => counters.errors.add(1));
    socket.setTimeout(() => socket.close(), holdMs);
  });
}
