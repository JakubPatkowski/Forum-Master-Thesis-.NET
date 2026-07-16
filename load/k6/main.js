// Phase 9c — the single k6 entrypoint. Profiles: smoke | demo | stress (PROFILE env), each a weighted
// realistic traffic mix over seeded data plus (demo/stress) a parallel WebSocket-holding scenario.
// Runs on the WSL host, outside the cluster, through ingress — see PHASE-9-10-ENTERPRISE-PLAN.md §9c.
//
//   k6 run -e PROFILE=demo -e BASE_URL=https://forum.local -e INGRESS_IP=$(minikube -p forum ip) load/k6/main.js
//
// Requires the Benchmark seed (bench_user_NNNN@bench.local / Bench#Password1); falls back to the
// Development seed's alice/bob/charlie for smoke-against-dev convenience. demo/stress additionally
// require the rate limiter raised (bench-run.sh does this) — any 429 fails the run via forum_rate_limited.

import { check, sleep } from 'k6';
import { Counter } from 'k6/metrics';
import ws from 'k6/ws';

import {
  addReaction, commentTree, commitUpload, createComment, createThread, initiateUpload, listCategories,
  login, reactionBatch, realtimeTicket, removeReaction, searchThreads, suggestTags, threadDetail,
  threadFeed, uploadToPresignedUrl, userStats, userThreads,
} from './lib/api.js';
import {
  PNG_1X1, PNG_1X1_BYTES, SEARCH_WORDS, TAG_PREFIXES, commentBody, randomTagSlugs, threadBody, threadTitle,
} from './lib/assets.js';

const PROFILE = __ENV.PROFILE || 'smoke';
const BASE_URL = __ENV.BASE_URL || 'https://forum.local';
const WS_URL = BASE_URL.replace(/^http/, 'ws');
// /etc/hosts on this box resolves forum.local to 127.0.0.1 first (Windows-tunnel entry), so the runner
// passes the minikube IP explicitly and k6 pins both ingress hosts to it — no sudo, no hosts-file edits.
const INGRESS_IP = __ENV.INGRESS_IP || '';

// Login pool: ordinary users only. Benchmark seeds 800 users; indices 0-1 admins, 2-11 moderators,
// 12-31 blocked (SeedPlan bands) — the pool starts at bench_user_0033 to keep every login an ordinary,
// non-blocked account. 200 users > 150 HTTP VUs + 40 WS VUs at stress, so each VU holds a distinct user.
const BENCH_USER_BASE = 33;
const BENCH_PASSWORD = 'Bench#Password1';
const DEV_USERS = ['alice', 'bob', 'charlie'];
const DEV_PASSWORD = 'Dev#Password1';
const LOGIN_STAGGER_MS = Number(__ENV.LOGIN_STAGGER_MS || 250); // ≤5 rps against the tight Auth limiter

// Custom metrics. forum_rate_limited: a 429 anywhere means the limiter raise didn't apply — the run is
// measuring the rate limiter, not the backend, and must fail (thresholded count<1 on every profile).
const rateLimited = new Counter('forum_rate_limited');
const wsSubscribed = new Counter('forum_ws_subscribed');
const wsNotifications = new Counter('forum_ws_notifications');
const wsErrors = new Counter('forum_ws_errors');

// Plateaus ≥ 90 s so the HPA (15 s metrics interval + stabilization window) visibly steps. VU peaks are
// sized for THIS host: 10 GiB minikube VM + ~2 GiB WSL slack shared by k6 (§1 resource contract) — k6 at
// 150 VU measured well under that; if the WSL VM ever swaps during stress, reduce the 150 before anything else.
const PROFILES = {
  smoke: {
    loginPool: 10,
    http: { executor: 'constant-vus', vus: 5, duration: '60s' },
    ws: null,
    thresholds: {
      'http_req_failed{scenario:http}': ['rate<0.01'],
      'http_req_duration{scenario:http}': ['p(95)<500'],
    },
  },
  demo: {
    loginPool: 200,
    http: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '1m', target: 10 },   // gentle start: JIT/pools already warm from the bench warm-up
        { duration: '30s', target: 40 },
        { duration: '2m', target: 40 },   // plateau 1 → HPA step
        { duration: '30s', target: 80 },
        { duration: '2m', target: 80 },   // plateau 2 → HPA step
        { duration: '1m', target: 0 },
      ],
    },
    ws: { vus: 20, duration: '7m' },
    thresholds: {
      'http_req_failed{scenario:http}': ['rate<0.02'],
      'http_req_duration{scenario:http}': ['p(95)<800'],
      forum_ws_subscribed: ['count>0'],
    },
  },
  stress: {
    loginPool: 200,
    http: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '30s', target: 50 },
        { duration: '1m', target: 50 },
        { duration: '30s', target: 100 },
        { duration: '1m', target: 100 },
        { duration: '30s', target: 150 },
        { duration: '2m', target: 150 },  // the knee-finding peak
        { duration: '1m', target: 0 },
      ],
    },
    ws: { vus: 40, duration: '6m30s' },
    // Informational: stress documents the knee, it must not abort. k6 only sets a non-zero exit code on
    // breach (no abortOnFail anywhere) and run-load-test.sh treats that exit code as "completed, thresholds
    // breached" — the numbers still land in the summary.
    thresholds: {
      'http_req_failed{scenario:http}': ['rate<0.05'],
      'http_req_duration{scenario:http}': ['p(95)<2000'],
      forum_ws_subscribed: ['count>0'],
    },
  },
};

const profile = PROFILES[PROFILE];
if (!profile) {
  throw new Error(`Unknown PROFILE '${PROFILE}' (use smoke|demo|stress)`);
}

// The traffic-mix endpoints, as { endpoint } tag values. Referencing each as a sub-metric threshold
// (always-true) materializes per-endpoint trends in the summary — that is what feeds the thesis tables.
const ENDPOINTS = [
  'login', 'categories', 'feed', 'thread', 'comments', 'reactions_batch', 'search', 'tags',
  'comment_create', 'thread_create', 'reaction_add', 'reaction_remove', 'file_initiate', 'file_put',
  'file_commit', 'user_stats', 'user_threads', 'ticket',
];

function buildOptions() {
  const scenarios = { http: { ...profile.http, exec: 'traffic' } };
  if (profile.ws) {
    scenarios.ws = { executor: 'constant-vus', vus: profile.ws.vus, duration: profile.ws.duration, exec: 'wsHold' };
  }

  const thresholds = { ...profile.thresholds, forum_rate_limited: ['count<1'] };
  for (const endpoint of ENDPOINTS) {
    thresholds[`http_req_duration{endpoint:${endpoint}}`] = ['max>=0']; // informational, never fails
  }

  const options = {
    scenarios,
    thresholds,
    summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(95)', 'p(99)', 'count'],
    setupTimeout: '15m', // 200 staggered Argon2id logins ≈ 2 min; generous headroom
    userAgent: 'forum-bench-k6/1.0',
  };
  if (INGRESS_IP) {
    options.hosts = { 'forum.local': INGRESS_IP, 'minio.forum.local': INGRESS_IP };
  }
  return options;
}

export const options = buildOptions();

// --- setup: login pool + seeded-data discovery (runs once, excluded from {scenario:http} thresholds) ---

export function setup() {
  const poolSize = Number(__ENV.LOGIN_POOL || profile.loginPool);

  // Probe the first Benchmark account; 401 → Development seed fallback (alice/bob/charlie).
  const probe = login(BASE_URL, `bench_user_${String(BENCH_USER_BASE + 1).padStart(4, '0')}@bench.local`, BENCH_PASSWORD);
  if (probe.status === 429) {
    throw new Error('Auth rate limiter hit on the FIRST login — raise RateLimiting__Auth__PermitLimit (bench-run.sh does) and retry.');
  }
  const devMode = probe.token === null;
  if (devMode) {
    console.warn('Benchmark users not found — falling back to the Development seed (alice/bob/charlie). Seed the Benchmark profile for measured runs.');
  }

  const tokens = devMode ? [] : [probe.token];
  const wanted = devMode ? DEV_USERS.length : poolSize;
  for (let i = tokens.length; i < wanted; i++) {
    const email = devMode
      ? `${DEV_USERS[i]}@dev.local`
      : `bench_user_${String(BENCH_USER_BASE + 1 + i).padStart(4, '0')}@bench.local`;
    const res = login(BASE_URL, email, devMode ? DEV_PASSWORD : BENCH_PASSWORD);
    if (res.status === 429) {
      throw new Error(`Auth rate limiter hit after ${tokens.length} logins — raise RateLimiting__Auth__PermitLimit (bench-run.sh does) and retry.`);
    }
    if (res.token) {
      tokens.push(res.token);
    }
    sleep(LOGIN_STAGGER_MS / 1000);
  }
  if (tokens.length === 0) {
    throw new Error('No logins succeeded — is the database seeded? (make seed ARGS="--benchmark --cluster")');
  }

  // Public categories only: the pool users are ordinary accounts, and a private-category 403 would
  // pollute http_req_failed with by-design rejections.
  const catRes = listCategories(BASE_URL);
  if (catRes.status !== 200) {
    throw new Error(`GET /api/content/categories failed (${catRes.status}) — cannot build the traffic pools.`);
  }
  const categories = catRes.json().filter((c) => c.visibility === 'public').map((c) => ({ id: c.id, slug: c.slug }));
  if (categories.length === 0) {
    throw new Error('No public categories found — seed the database first.');
  }

  // Thread pool: two keyset pages per category (≤100 threads each). Feed items carry ownerId, which
  // later doubles as the seeded-user pool for the profile/stats mix (no separate user discovery call).
  const threadsByCategory = {};
  for (const cat of categories) {
    const threads = [];
    let cursor = null;
    for (let page = 0; page < 2; page++) {
      const res = threadFeed(BASE_URL, cat.id, cursor, 50);
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
    threadsByCategory[cat.id] = threads;
  }

  const total = Object.values(threadsByCategory).reduce((n, t) => n + t.length, 0);
  if (total === 0) {
    throw new Error('No threads discovered in any public category — seed the database first.');
  }
  console.log(`setup: ${tokens.length} tokens, ${categories.length} public categories, ${total} threads pooled${devMode ? ' [DEV SEED]' : ''}`);

  return { tokens, categories, threadsByCategory };
}

// --- selection helpers (Zipf-ish: mirrors the seeder's 50%-hot-categories skew + hot threads) ---

function pickCategory(data) {
  const hot = Math.min(3, data.categories.length);
  const idx = Math.random() < 0.5 ? Math.floor(Math.random() * hot) : Math.floor(Math.random() * data.categories.length);
  return data.categories[idx];
}

function pickThread(data) {
  for (let attempt = 0; attempt < 5; attempt++) {
    const cat = pickCategory(data);
    const threads = data.threadsByCategory[cat.id];
    if (!threads || threads.length === 0) {
      continue;
    }
    // 30% hot pick from the feed head (pinned + newest) — keyset page 1 is the real-world hot set.
    const idx = Math.random() < 0.3
      ? Math.floor(Math.random() * Math.min(5, threads.length))
      : Math.floor(Math.random() * threads.length);
    return { category: cat, thread: threads[idx] };
  }
  const cat = data.categories[0];
  return { category: cat, thread: data.threadsByCategory[cat.id][0] };
}

function vuToken(data) {
  return data.tokens[(__VU - 1) % data.tokens.length];
}

/** Records the check + counts 429s (any 429 = the limiter raise didn't apply = the run is invalid). */
function track(res, name, okStatuses = [200]) {
  if (res.status === 429) {
    rateLimited.add(1);
  }
  check(res, { [`${name} ok`]: (r) => okStatuses.includes(r.status) });
  return okStatuses.includes(res.status);
}

// --- the weighted HTTP mix (one action per iteration + 0.3–0.7 s think time) ---

export function traffic(data) {
  const token = vuToken(data);
  const roll = Math.random() * 100;

  if (roll < 30) {
    // 30% browse a category feed; 30% of those follow the keyset cursor to page 2.
    const cat = pickCategory(data);
    const first = threadFeed(BASE_URL, cat.id, null, 20);
    if (track(first, 'feed') && Math.random() < 0.3) {
      const body = first.json();
      if (body.hasMore && body.nextCursor) {
        track(threadFeed(BASE_URL, cat.id, body.nextCursor, 20), 'feed');
      }
    }
  } else if (roll < 50) {
    // 20% open a thread — the SPA's real 3-call pattern (G22 parity): detail + tree + reaction batch.
    const { thread } = pickThread(data);
    track(threadDetail(BASE_URL, thread.id), 'thread');
    const tree = commentTree(BASE_URL, thread.id);
    let batchIds = [thread.id];
    let batchType = 'thread';
    if (track(tree, 'comments')) {
      const comments = tree.json();
      if (comments.length > 0) {
        batchIds = comments.slice(0, 50).map((c) => c.id);
        batchType = 'comment';
      }
    }
    track(reactionBatch(BASE_URL, batchType, batchIds), 'reactions_batch');
  } else if (roll < 60) {
    // 10% FTS search with words the seeder actually wrote (guaranteed corpus hits).
    track(searchThreads(BASE_URL, SEARCH_WORDS[Math.floor(Math.random() * SEARCH_WORDS.length)]), 'search');
  } else if (roll < 68) {
    // 8% tag autocomplete.
    track(suggestTags(BASE_URL, TAG_PREFIXES[Math.floor(Math.random() * TAG_PREFIXES.length)]), 'tags');
  } else if (roll < 78) {
    // 10% create a top-level comment (replies risk the depth-5 422 on seeded depth-4 chains).
    const { thread } = pickThread(data);
    track(createComment(BASE_URL, token, thread.id, commentBody(Math.random)), 'comment_create', [200, 201]);
  } else if (roll < 83) {
    // 5% create a thread with 1–3 existing seeded tags (get-or-create → no unbounded tag growth).
    const cat = pickCategory(data);
    track(createThread(BASE_URL, token, cat.id, threadTitle(Math.random), threadBody(Math.random), randomTagSlugs(Math.random)),
      'thread_create', [200, 201]);
  } else if (roll < 95) {
    // 12% reaction toggle — both directions are idempotent 200s, so no like-state bookkeeping needed.
    const { thread } = pickThread(data);
    if (Math.random() < 0.5) {
      track(addReaction(BASE_URL, token, thread.id), 'reaction_add');
    } else {
      track(removeReaction(BASE_URL, token, thread.id), 'reaction_remove');
    }
  } else if (roll < 98) {
    // 3% ADR 0008 golden path: initiate → presigned PUT to MinIO (via ingress) → commit (ImageProbe).
    const initiate = initiateUpload(BASE_URL, token, 'image/png', PNG_1X1_BYTES);
    if (track(initiate, 'file_initiate', [200, 201])) {
      const uploadUrl = initiate.json('uploadUrl');
      const fileId = initiate.json('fileId');
      if (track(uploadToPresignedUrl(uploadUrl, PNG_1X1, 'image/png'), 'file_put')) {
        track(commitUpload(BASE_URL, token, fileId), 'file_commit');
      }
    }
  } else {
    // 2% profile page: stats + the user's threads (the SPA's /u/[userId] calls). Thread owners double
    // as the seeded-user pool.
    const { thread } = pickThread(data);
    track(userStats(BASE_URL, thread.ownerId), 'user_stats');
    track(userThreads(BASE_URL, thread.ownerId), 'user_threads');
  }

  sleep(0.3 + Math.random() * 0.4); // fairness checklist: same think time as Architecture B's runs
}

// --- the WS-holding scenario (demo/stress, parallel with HTTP — ADR 0010 + G13 under load) ---

const WS_HOLD_MS = Number(__ENV.WS_HOLD_MS || 120_000); // > 60 s: an idle drop at exactly 60 s = G13 regression

export function wsHold(data) {
  const token = vuToken(data);
  const ticketRes = realtimeTicket(BASE_URL, token);
  if (!track(ticketRes, 'ticket')) {
    sleep(3);
    return;
  }
  const ticket = ticketRes.json('ticket');
  const category = pickCategory(data);

  const res = ws.connect(`${WS_URL}/api/realtime/ws?ticket=${encodeURIComponent(ticket)}`, {}, (socket) => {
    socket.on('open', () => {
      socket.send(JSON.stringify({ action: 'subscribe', view: 'category', id: category.id }));
    });
    socket.on('message', (raw) => {
      const msg = JSON.parse(raw);
      if (msg.type === 'subscribed') {
        wsSubscribed.add(1);
      } else if (msg.type === 'error') {
        wsErrors.add(1);
      } else if (msg.entity) {
        wsNotifications.add(1); // a change notification pushed for the subscribed category
      }
    });
    socket.on('error', () => wsErrors.add(1));
    // Hold across the 60 s idle boundary, then reconnect on the next iteration with a fresh single-use
    // ticket — each cycle re-exercises mint → handshake → subscribe under load.
    socket.setTimeout(() => socket.close(), WS_HOLD_MS);
  });
  check(res, { 'ws handshake 101': (r) => r && r.status === 101 });
}

// --- summary: human-readable per-endpoint table + the machine-readable marker block ---

function fmt(value) {
  return value === undefined || value === null ? '-' : String(Math.round(value));
}

export function handleSummary(data) {
  const lines = [];
  lines.push(`\nprofile=${PROFILE}  base=${BASE_URL}`);

  const totals = data.metrics['http_req_duration{scenario:http}'] || data.metrics.http_req_duration;
  const failed = data.metrics['http_req_failed{scenario:http}'] || data.metrics.http_req_failed;
  const reqs = data.metrics.http_reqs;
  if (reqs && totals) {
    lines.push(`http_reqs=${reqs.values.count}  rps=${reqs.values.rate.toFixed(1)}  ` +
      `p95=${fmt(totals.values['p(95)'])}ms  p99=${fmt(totals.values['p(99)'])}ms  ` +
      `failed=${failed ? (failed.values.rate * 100).toFixed(2) : '?'}%`);
  }

  lines.push('');
  lines.push('endpoint          count    avg    med  p(95)  p(99)    max');
  for (const endpoint of ENDPOINTS) {
    const m = data.metrics[`http_req_duration{endpoint:${endpoint}}`];
    if (!m || !m.values.count) {
      continue;
    }
    const v = m.values;
    lines.push(
      endpoint.padEnd(16) +
      String(v.count).padStart(7) + fmt(v.avg).padStart(7) + fmt(v.med).padStart(7) +
      fmt(v['p(95)']).padStart(7) + fmt(v['p(99)']).padStart(7) + fmt(v.max).padStart(7));
  }

  const wsSub = data.metrics.forum_ws_subscribed;
  const wsNotif = data.metrics.forum_ws_notifications;
  const wsErr = data.metrics.forum_ws_errors;
  if (wsSub || wsNotif) {
    lines.push('');
    lines.push(`ws: subscribed=${wsSub ? wsSub.values.count : 0}  notifications=${wsNotif ? wsNotif.values.count : 0}  errors=${wsErr ? wsErr.values.count : 0}`);
  }
  const limited = data.metrics.forum_rate_limited;
  if (limited && limited.values.count > 0) {
    lines.push(`\n!!! ${limited.values.count} responses were 429 — the rate-limiter raise did not apply; this run is INVALID.`);
  }

  const failures = Object.entries(data.metrics)
    .filter(([, m]) => m.thresholds && Object.values(m.thresholds).some((t) => !t.ok))
    .map(([name]) => name);
  lines.push(failures.length > 0 ? `\nTHRESHOLDS BREACHED: ${failures.join(', ')}` : '\nall thresholds passed');

  // The marker block is what run-load-test.sh extracts into load/results/summary-<stamp>.json.
  const text = `${lines.join('\n')}\n\n===K6_SUMMARY_JSON_BEGIN===\n${JSON.stringify(data)}\n===K6_SUMMARY_JSON_END===\n`;
  return { stdout: text };
}
