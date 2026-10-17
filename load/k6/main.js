// The single k6 entrypoint for BOTH architectures (A/B thesis harness — AB-ALIGNMENT-INSTRUCTIONS-A.md A-01,
// forum-spec §9). Everything that defines the *experiment* lives here and is shared: profiles (smoke | warmup |
// demo | stress | capacity), the journey mix + think time, journey metrics, thresholds and the summary blocks.
// Everything that touches a wire format lives in a target adapter (targets/a.js = JSON API + SPA, targets/b.js =
// SSR HTML), selected with TARGET=a|b. One journey = one user-visible action (1 SPA journey = N JSON calls on A
// vs 1 HTML round-trip on B) — journeys/sec and per-journey latency are the comparable unit, raw RPS is not.
//
//   k6 run -e TARGET=a -e PROFILE=demo -e BASE_URL=https://forum.local -e INGRESS_IP=$(minikube -p forum ip) load/k6/main.js
//   k6 run -e TARGET=b -e PROFILE=demo -e BASE_URL=https://gomx.local  -e INGRESS_IP=$(minikube -p forum ip) load/k6/main.js
//
// Requires the Benchmark seed on the target (A: bench_user_NNNN / Bench#Password1; B: benchuserNNNN /
// DemoPassword1); each adapter falls back to its Development/demo seed for smoke-against-dev convenience.
// demo/stress/capacity additionally require the rate limiters neutralized (A: raised by bench-run.sh / ab-round.sh;
// B: the bench overlay sets GOMX_AUTH_RATELIMIT=0) — any 429 fails the run via forum_rate_limited.
//
// Two load models (the thesis needs both):
//   CLOSED (smoke, demo, stress): a fixed number of VUs with 0.3–0.7 s think time. Throughput is then set by the
//     VU count, so journeys/s is (nearly) equal on both targets BY CONSTRUCTION — these profiles answer H1
//     (resources per journey at equal load) and give latency under equal concurrency.
//   OPEN (capacity): ramping-arrival-rate in steps of journeys/s, no think time. Throughput is the INPUT, so
//     the step where journey p95 crosses 500 ms / failures exceed 1 % / the achieved rate falls behind the
//     offered one is the throughput ceiling of H2. Every request of an iteration is tagged with its step.

import { check, sleep } from 'k6';
import exec from 'k6/execution';
import { Counter, Trend } from 'k6/metrics';

import { commentBody } from './lib/assets.js';
import * as targetA from './targets/a.js';
import * as targetB from './targets/b.js';

const TARGET = (__ENV.TARGET || 'a').toLowerCase();
const TARGETS = { a: targetA, b: targetB };
const target = TARGETS[TARGET];
if (!target) {
  throw new Error(`Unknown TARGET '${TARGET}' (use a|b)`);
}

const PROFILE = __ENV.PROFILE || 'smoke';
const BASE_URL = __ENV.BASE_URL || target.DEFAULT_BASE_URL;
// /etc/hosts on this box resolves the ingress hosts to 127.0.0.1 first (Windows-tunnel entry), so the runner
// passes the minikube IP explicitly and k6 pins the hosts to it — no sudo, no hosts-file edits.
const INGRESS_IP = __ENV.INGRESS_IP || '';
const LOGIN_STAGGER_MS = Number(__ENV.LOGIN_STAGGER_MS || 250); // ≤4 logins/s against A's tight Auth limiter
const WS_HOLD_MS = Number(__ENV.WS_HOLD_MS || 120_000); // > 60 s: an idle drop at exactly 60 s = G13 regression (A)

// --- versions recorded with every result -----------------------------------------------------------------------
// MIX_VERSION: the journey weights (a thesis table never mixes two weightings).
//   v0-day0 (2026-10-17 night): Day-0 weights; pool = newest 2×50 (A) / 2×20 (B) threads per space.
//   v1 (2026-10-17 evening): SAME weights; harness fixes that change what a journey does → not comparable to v0:
//     shared search words on both targets, whole-corpus thread pools, A J5 = real toggle, gzip on every request,
//     scenario-only time windows. J4 (+image), J8-as-metric and J9 stay weight 0 (B adapter lacks them, P1).
// HARNESS_VERSION: bumps whenever adapter behaviour changes without a weight change.
export const MIX_VERSION = 'v1';
export const HARNESS_VERSION = 'h3-2026-10-17';
const MIX = [
  ['J1', 33], // space feed (page + 30% cursor page 2)
  ['J2', 22], // thread open
  ['J6', 11], // search
  ['nav', 8], // list spaces
  ['J3', 11], // comment create (authenticated)
  ['J5', 13], // reaction toggle (authenticated)
  ['J7', 2],  // profile
];
// Mix of the discarded warm-up (60 s, 10 VUs, before EVERY measured run): every measured code path must be warm
// — including the write paths (the 2026-10-17 dry run with a read-only warm-up showed A's J3/J5 p99 at 150–175 ms
// from tiered-JIT compilation inside the measured window; Go has no JIT). The write share is kept tiny so the
// dataset moves by only ~30 comments + ~30 reactions — the same on both sides, and run.json records the exact
// counts the measured run starts from.
const WARMUP_MIX = [
  ['J1', 40],
  ['J2', 27],
  ['J6', 13],
  ['nav', 10],
  ['J7', 4],
  ['J3', 3],
  ['J5', 3],
];
const JOURNEYS = ['J1', 'J2', 'J3', 'J4', 'J5', 'J6', 'J7', 'J8', 'J9', 'nav'];

// --- custom metrics ------------------------------------------------------------------------------------------
const wsSubscribed = new Counter('forum_ws_subscribed');
const wsNotifications = new Counter('forum_ws_notifications');
const wsErrors = new Counter('forum_ws_errors');
const WS_COUNTERS = { subscribed: wsSubscribed, notifications: wsNotifications, errors: wsErrors };
const journeys = new Counter('forum_journeys');
const journeyDuration = new Trend('forum_journey_duration', true);

/** Runs one journey: tags every request in fn with {journey}, times it, counts it. */
function runJourney(name, fn) {
  exec.vu.tags.journey = name;
  const started = Date.now();
  try {
    fn();
  } finally {
    journeyDuration.add(Date.now() - started, { journey: name });
    journeys.add(1, { journey: name });
    delete exec.vu.tags.journey;
  }
}

// --- capacity (open model) step plan -------------------------------------------------------------------------
// Offered load in journeys/s per step; each step = a short ramp (tagged 'ramp', excluded) + a hold (tagged
// c01, c02, …). Defaults finish in 8 min so setup (~2.5 min of A logins) + run stays inside A's 15-min access-token
// lifetime. Override with CAP_STEPS="25,50,…" CAP_RAMP_S=15 CAP_HOLD_S=45 CAP_MAX_VUS=300.
const CAP_STEPS = (__ENV.CAP_STEPS || '25,50,100,150,200,300,400,500').split(',').map(Number).filter((n) => n > 0);
const CAP_RAMP_S = Number(__ENV.CAP_RAMP_S || 15);
const CAP_HOLD_S = Number(__ENV.CAP_HOLD_S || 45);
const CAP_MAX_VUS = Number(__ENV.CAP_MAX_VUS || 300);
const CAP_PLAN = CAP_STEPS.map((rate, i) => ({
  tag: `c${String(i + 1).padStart(2, '0')}`,
  rate,
  holdStartS: i * (CAP_RAMP_S + CAP_HOLD_S) + CAP_RAMP_S,
  holdEndS: (i + 1) * (CAP_RAMP_S + CAP_HOLD_S),
}));
const CAP_TOTAL_S = CAP_STEPS.length * (CAP_RAMP_S + CAP_HOLD_S);

// --- profiles ------------------------------------------------------------------------------------------------
// Plateaus ≥ 90 s so an HPA (when enabled) visibly steps. VU peaks are sized for THIS host: 10 GiB minikube VM
// + ~2 GiB WSL slack shared by k6 — k6 at 150 VU measured well under that.
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
  // Discarded before every measured run (ab-round.sh): all journeys warm, ~6 % writes (see WARMUP_MIX).
  warmup: {
    loginPool: 10,
    mix: WARMUP_MIX,
    http: { executor: 'constant-vus', vus: 10, duration: '60s' },
    ws: null,
    thresholds: { 'http_req_failed{scenario:http}': ['rate<0.05'] },
  },
  demo: {
    loginPool: 200,
    http: {
      executor: 'ramping-vus',
      startVUs: 0,
      stages: [
        { duration: '1m', target: 10 },   // gentle start: JIT/pools already warm from the bench warm-up
        { duration: '30s', target: 40 },
        { duration: '2m', target: 40 },   // plateau 1
        { duration: '30s', target: 80 },
        { duration: '2m', target: 80 },   // plateau 2
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
    // Informational: stress documents the knee, it must not abort (run-load-test.sh maps rc 99 → 0 for stress).
    thresholds: {
      'http_req_failed{scenario:http}': ['rate<0.05'],
      'http_req_duration{scenario:http}': ['p(95)<2000'],
      forum_ws_subscribed: ['count>0'],
    },
  },
  capacity: {
    loginPool: 200,
    openModel: true,
    http: {
      executor: 'ramping-arrival-rate',
      startRate: Math.max(1, Math.floor((CAP_STEPS[0] || 10) / 2)),
      timeUnit: '1s',
      preAllocatedVUs: 50,
      maxVUs: CAP_MAX_VUS,
      stages: CAP_STEPS.flatMap((rate) => [
        { duration: `${CAP_RAMP_S}s`, target: rate },
        { duration: `${CAP_HOLD_S}s`, target: rate },
      ]),
    },
    ws: { vus: 20, duration: `${CAP_TOTAL_S}s` },
    // Informational (run-load-test.sh maps rc 99 → 0): the per-step table in handleSummary finds the knee.
    thresholds: {
      'http_req_failed{scenario:http}': ['rate<=1'],
      'http_req_duration{scenario:http}': ['max>=0'],
    },
  },
};

const profile = PROFILES[PROFILE];
if (!profile) {
  throw new Error(`Unknown PROFILE '${PROFILE}' (use ${Object.keys(PROFILES).join('|')})`);
}
const ACTIVE_MIX = profile.mix || MIX;
const MIX_TOTAL = ACTIVE_MIX.reduce((sum, [, w]) => sum + w, 0);
if (MIX_TOTAL !== 100) {
  throw new Error(`MIX weights sum to ${MIX_TOTAL}, expected 100`);
}
for (const [name] of ACTIVE_MIX) {
  if (typeof target.journeys[name] !== 'function') {
    throw new Error(`Target '${TARGET}' does not implement journey '${name}' required by mix ${MIX_VERSION}`);
  }
}

/** Nominal length of the http scenario in seconds — the denominator of journeys/s (never the setup time). */
function scenarioSeconds() {
  const parse = (d) => {
    const m = String(d).match(/^(?:(\d+)m)?(?:(\d+)s)?$/);
    return m ? Number(m[1] || 0) * 60 + Number(m[2] || 0) : 0;
  };
  if (profile.http.stages) {
    return profile.http.stages.reduce((s, st) => s + parse(st.duration), 0);
  }
  return parse(profile.http.duration);
}
const SCENARIO_S = scenarioSeconds();

function buildOptions() {
  const scenarios = { http: { ...profile.http, exec: 'traffic' } };
  if (profile.ws) {
    scenarios.ws = { executor: 'constant-vus', vus: profile.ws.vus, duration: profile.ws.duration, exec: 'wsHold' };
  }

  // data_received/data_sent of the http scenario only (setup's login + pool crawl excluded) → bytes per journey on
  // the wire (gzip as negotiated, TLS included) — the transfer side of H1.
  const thresholds = {
    ...profile.thresholds,
    forum_rate_limited: ['count<1'],
    'data_received{scenario:http}': ['count>=0'],
    'data_sent{scenario:http}': ['count>=0'],
  };
  for (const endpoint of target.ENDPOINTS) {
    thresholds[`http_req_duration{endpoint:${endpoint}}`] = ['max>=0']; // informational, never fails
  }
  for (const j of JOURNEYS) {
    // Informational: materializes the per-journey sub-metrics (journeys/sec + duration) in the summary.
    thresholds[`forum_journeys{journey:${j}}`] = ['count>=0'];
    thresholds[`forum_journey_duration{journey:${j}}`] = ['max>=0'];
  }
  if (profile.openModel) {
    for (const step of CAP_PLAN) {
      thresholds[`forum_journeys{step:${step.tag}}`] = ['count>=0'];
      thresholds[`forum_journey_duration{step:${step.tag}}`] = ['max>=0'];
      thresholds[`http_req_failed{step:${step.tag}}`] = ['rate>=0'];
      thresholds[`http_req_duration{step:${step.tag}}`] = ['max>=0'];
    }
  }

  return {
    scenarios,
    thresholds,
    summaryTrendStats: ['avg', 'min', 'med', 'max', 'p(90)', 'p(95)', 'p(99)', 'count'],
    setupTimeout: '15m', // 200 staggered logins + the whole-corpus pool crawl; generous headroom
    userAgent: 'forum-bench-k6/2.0',
    hosts: target.hosts(INGRESS_IP),
  };
}

export const options = buildOptions();

// --- setup: delegated to the target (login pool + seeded-data discovery, excluded from {scenario:http}) ------

export function setup() {
  const poolSize = Number(__ENV.LOGIN_POOL || profile.loginPool);
  const data = target.setup(BASE_URL, poolSize, LOGIN_STAGGER_MS);
  const total = Object.values(data.threadsBySpace).reduce((n, t) => n + t.length, 0);
  if (total === 0) {
    throw new Error('No threads discovered in any public space — seed the database first.');
  }
  console.log(`setup[${TARGET}]: ${data.sessions.length} sessions, ${data.spaces.length} public spaces, ${total} threads pooled${data.devMode ? ' [DEV SEED]' : ''}`);
  // The measured window starts HERE: resources and journeys/s exclude the setup phase (200 logins = Argon2id
  // CPU on A vs PBKDF2 on B — Day-0 averaged it into both numbers).
  data.setupEndedAtMs = Date.now();
  return data;
}

// --- selection helpers (target-neutral by construction; computed once per VU — each VU has its own runtime) -----
//
// SPACE (J1 feed, J8 subscription): 50 % one of the three LARGEST public spaces, 50 % uniform over all of them.
//   "Largest" is measured from the crawled pools, never taken from list order: B lists spaces by name, so "the
//   first three" were B's three SMALLEST spaces while A's were its largest (found in the v1 review).
// THREAD (J2, J3, J5, J7): uniform over the WHOLE public corpus. Any rule tied to feed position or space size
//   samples each seed's layout instead of the workload: the single heaviest thread (≈230 comments) sits pinned in
//   a small space on A but in the largest space on B, so a "30 % from page 1 of the picked space" rule opened it
//   ≈3× more often on B (≈1.4 % vs ≈0.45 % of J2 — exactly p99 territory). Uniform draws make every thread equally
//   likely on both sides; the opened-thread comment distribution then equals the seed's (mean 5.73 A vs 5.72 B).

let spacesBySize = null;
let allThreads = null;

function pickSpace(data) {
  if (spacesBySize === null) {
    const size = (space) => (data.threadsBySpace[space.key] || []).length;
    spacesBySize = [...data.spaces].sort((x, y) => size(y) - size(x));
  }
  const hot = Math.min(3, spacesBySize.length);
  const idx = Math.random() < 0.5 ? Math.floor(Math.random() * hot) : Math.floor(Math.random() * spacesBySize.length);
  return spacesBySize[idx];
}

function pickThread(data) {
  if (allThreads === null) {
    allThreads = Object.values(data.threadsBySpace).flat();
  }
  return { thread: allThreads[Math.floor(Math.random() * allThreads.length)] };
}

const pick = {
  space: pickSpace,
  thread: pickThread,
  word: () => target.SEARCH_WORDS[Math.floor(Math.random() * target.SEARCH_WORDS.length)],
  commentBody: () => commentBody(Math.random),
};

function vuSession(data) {
  return data.sessions[(__VU - 1) % data.sessions.length];
}

/** Capacity profile: the step tag of the CURRENT moment of the http scenario ('ramp' between holds). */
function currentStepTag() {
  const elapsedS = (Date.now() - exec.scenario.startTime) / 1000;
  const step = CAP_PLAN.find((s) => elapsedS >= s.holdStartS && elapsedS < s.holdEndS);
  return step ? step.tag : 'ramp';
}

// --- the weighted HTTP mix (one journey per iteration + 0.3–0.7 s think time in the closed model) -------------

export function traffic(data) {
  const session = vuSession(data);
  if (profile.openModel) {
    exec.vu.tags.step = currentStepTag();
  }
  let roll = Math.random() * MIX_TOTAL;
  for (const [name, weight] of ACTIVE_MIX) {
    if (roll < weight) {
      runJourney(name, () => target.journeys[name](BASE_URL, data, session, pick));
      break;
    }
    roll -= weight;
  }
  if (profile.openModel) {
    delete exec.vu.tags.step;
    return; // open model: the arrival rate is the input — think time would only hold VUs, not change the load
  }
  sleep(0.3 + Math.random() * 0.4); // fairness checklist: identical think time on both targets
}

// --- the WS-holding scenario (demo/stress/capacity, parallel with HTTP) ----------------------------------------

export function wsHold(data) {
  // J8: one hold cycle = one journey; its duration is the hold length (receipt latency is the
  // forum_ws_notifications stream vs writes, not this trend).
  exec.vu.tags.journey = 'J8';
  const session = vuSession(data);
  const res = target.wsHold(BASE_URL, data, session, WS_HOLD_MS, WS_COUNTERS, pick);
  if (res === null) {
    delete exec.vu.tags.journey;
    sleep(3); // ticket/handshake prerequisite failed (already tracked by the adapter) — back off
    return;
  }
  check(res, { 'ws handshake 101': (r) => r && r.status === 101 });
  journeys.add(1, { journey: 'J8' });
  delete exec.vu.tags.journey;
}

// --- summary: human-readable tables + the machine-readable marker blocks ---------------------------------------

function fmt(value) {
  return value === undefined || value === null ? '-' : String(Math.round(value));
}

function round3(value) {
  return value === undefined || value === null ? null : Number(value.toFixed(3));
}

/** Per-step open-model table + knee: first step whose p95 > 500 ms, failures > 1 % or achieved < 95 % of offered. */
function capacitySummary(data, lines) {
  const steps = [];
  let knee = null;
  lines.push('');
  lines.push('capacity step  offered  achieved   p50    p95    p99  failed%');
  for (const step of CAP_PLAN) {
    const c = data.metrics[`forum_journeys{step:${step.tag}}`];
    const d = data.metrics[`forum_journey_duration{step:${step.tag}}`];
    const f = data.metrics[`http_req_failed{step:${step.tag}}`];
    const count = c ? c.values.count : 0;
    const achieved = count / CAP_HOLD_S;
    const dv = d ? d.values : {};
    const failedPct = f ? f.values.rate * 100 : null;
    const row = {
      step: step.tag, offered_jps: step.rate, achieved_jps: round3(achieved), journeys: count,
      duration_ms: { med: dv.med ?? null, p95: dv['p(95)'] ?? null, p99: dv['p(99)'] ?? null },
      failed_pct: round3(failedPct),
    };
    const breached = (dv['p(95)'] ?? 0) > 500 || (failedPct ?? 0) > 1 || achieved < 0.95 * step.rate;
    if (breached && knee === null) {
      knee = { step: step.tag, offered_jps: step.rate, reason: (dv['p(95)'] ?? 0) > 500 ? 'p95>500ms'
        : (failedPct ?? 0) > 1 ? 'failed>1%' : 'achieved<95%' };
    }
    steps.push(row);
    lines.push(
      `  ${step.tag}`.padEnd(14) + String(step.rate).padStart(8) + achieved.toFixed(1).padStart(10) +
      fmt(dv.med).padStart(6) + fmt(dv['p(95)']).padStart(7) + fmt(dv['p(99)']).padStart(7) +
      (failedPct === null ? '-' : failedPct.toFixed(2)).padStart(9) + (breached ? '  <-' : ''));
  }
  // Sustained = the highest offered step BEFORE the knee (every step when the knee was never reached).
  const kneeIdx = knee ? steps.findIndex((s) => s.step === knee.step) : steps.length;
  const sustained = kneeIdx > 0 ? steps[kneeIdx - 1].offered_jps : null;
  const dropped = data.metrics.dropped_iterations ? data.metrics.dropped_iterations.values.count : 0;
  lines.push(`knee: ${knee ? `${knee.step} @ ${knee.offered_jps} j/s (${knee.reason})` : 'not reached'}` +
    `   sustained: ${sustained === null ? '-' : `${sustained} j/s`}   dropped_iterations: ${dropped}`);
  return {
    steps, knee, sustained_jps: sustained, dropped_iterations: dropped,
    ramp_s: CAP_RAMP_S, hold_s: CAP_HOLD_S, max_vus: CAP_MAX_VUS,
  };
}

export function handleSummary(data) {
  const lines = [];
  const setupEndedAtMs = data.setup_data ? data.setup_data.setupEndedAtMs : null;
  lines.push(`\ntarget=${TARGET}  profile=${PROFILE}  mix=${MIX_VERSION}  harness=${HARNESS_VERSION}  base=${BASE_URL}`);

  const totals = data.metrics['http_req_duration{scenario:http}'] || data.metrics.http_req_duration;
  const failed = data.metrics['http_req_failed{scenario:http}'] || data.metrics.http_req_failed;
  const reqs = data.metrics.http_reqs;
  if (reqs && totals) {
    lines.push(`http_reqs=${reqs.values.count}  p95=${fmt(totals.values['p(95)'])}ms  p99=${fmt(totals.values['p(99)'])}ms  ` +
      `failed=${failed ? (failed.values.rate * 100).toFixed(2) : '?'}%  (scenario ${SCENARIO_S}s, setup excluded)`);
  }

  lines.push('');
  lines.push('endpoint          count    avg    med  p(95)  p(99)    max');
  for (const endpoint of target.ENDPOINTS) {
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

  // Per-journey table + the shared A/B journey-summary object (schema: forum-spec §9.4). per_second is computed
  // over the SCENARIO length (k6's own rate divides by setup+scenario — Day-0's 73.6 vs 76.4 j/s gap was only
  // A's longer Argon2id setup); the k6 rate is kept as per_second_k6 for traceability.
  const journeySummary = {};
  lines.push('');
  lines.push('journey    count   /sec    med  p(95)  p(99)   (duration ms; /sec over the scenario window)');
  for (const j of JOURNEYS) {
    const c = data.metrics[`forum_journeys{journey:${j}}`];
    if (!c || !c.values.count) {
      continue;
    }
    const d = data.metrics[`forum_journey_duration{journey:${j}}`];
    const dv = d ? d.values : {};
    const perSecond = SCENARIO_S > 0 ? c.values.count / SCENARIO_S : c.values.rate;
    journeySummary[j] = {
      count: c.values.count,
      per_second: round3(perSecond),
      per_second_k6: round3(c.values.rate),
      duration_ms: { med: dv.med ?? null, p95: dv['p(95)'] ?? null, p99: dv['p(99)'] ?? null },
    };
    lines.push(
      j.padEnd(9) + String(c.values.count).padStart(7) + perSecond.toFixed(2).padStart(7) +
      fmt(dv.med).padStart(7) + fmt(dv['p(95)']).padStart(7) + fmt(dv['p(99)']).padStart(7));
  }
  const all = data.metrics.forum_journeys;
  const allD = data.metrics.forum_journey_duration;
  const httpJourneys = Object.entries(journeySummary).filter(([j]) => j !== 'J8').reduce((n, [, v]) => n + v.count, 0);
  const received = data.metrics['data_received{scenario:http}'];
  const sent = data.metrics['data_sent{scenario:http}'];

  const capacity = profile.openModel ? capacitySummary(data, lines) : null;

  const wsSub = data.metrics.forum_ws_subscribed;
  const wsNotif = data.metrics.forum_ws_notifications;
  const wsErr = data.metrics.forum_ws_errors;
  if (wsSub || wsNotif) {
    lines.push('');
    lines.push(`ws: subscribed=${wsSub ? wsSub.values.count : 0}  notifications=${wsNotif ? wsNotif.values.count : 0}  errors=${wsErr ? wsErr.values.count : 0}`);
  }
  const limited = data.metrics.forum_rate_limited;
  if (limited && limited.values.count > 0) {
    lines.push(`\n!!! ${limited.values.count} responses were 429 — a rate limiter is active; this run is INVALID.`);
  }

  const failures = Object.entries(data.metrics)
    .filter(([, m]) => m.thresholds && Object.values(m.thresholds).some((t) => !t.ok))
    .map(([name]) => name);
  lines.push(failures.length > 0 ? `\nTHRESHOLDS BREACHED: ${failures.join(', ')}` : '\nall thresholds passed');

  // Window of the measured scenario (epoch ms): from the end of setup for the nominal scenario length. The
  // runner/orchestrator query Prometheus over exactly this window for CPU/memory per journey.
  const window = setupEndedAtMs
    ? { start_ms: setupEndedAtMs, end_ms: setupEndedAtMs + SCENARIO_S * 1000, scenario_s: SCENARIO_S,
      setup_s: round3((setupEndedAtMs - (Date.now() - data.state.testRunDurationMs)) / 1000) }
    : null;

  // Never archive credentials or the whole pool: the setup data (bearer tokens / session cookies + up to
  // 1 300 threads) is replaced by a small description before the summary JSON leaves k6.
  const pool = data.setup_data && data.setup_data.threadsBySpace
    ? Object.fromEntries(Object.entries(data.setup_data.threadsBySpace).map(([k, v]) => [k, v.length]))
    : null;
  const summaryData = {
    ...data,
    setup_data: data.setup_data ? {
      setupEndedAtMs, sessions: (data.setup_data.sessions || []).length, spaces: (data.setup_data.spaces || []).length,
      threads_by_space: pool, dev_mode: data.setup_data.devMode,
    } : null,
  };

  // Marker blocks extracted by run-load-test.sh: the full k6 summary → summary-<target>-<stamp>.json, the shared
  // A/B per-journey block → journeys-<target>-<stamp>.json. Both targets emit the same shape — one join.
  const journeysBlock = JSON.stringify({
    target: TARGET, profile: PROFILE, mix_version: MIX_VERSION, harness_version: HARNESS_VERSION,
    window, journeys: journeySummary,
    totals: {
      http_journeys: httpJourneys,
      http_journeys_per_second: SCENARIO_S > 0 ? round3(httpJourneys / SCENARIO_S) : null,
      all_journeys_k6: all ? all.values.count : 0,
      journey_duration_ms: allD ? { med: allD.values.med, p95: allD.values['p(95)'], p99: allD.values['p(99)'] } : null,
      http_reqs: reqs ? reqs.values.count : 0,
      http_req_failed_pct: failed ? round3(failed.values.rate * 100) : null,
      http_req_p95_ms: totals ? totals.values['p(95)'] : null,
      data_received_bytes: received ? received.values.count : null,
      data_sent_bytes: sent ? sent.values.count : null,
      kib_received_per_journey: received && httpJourneys ? round3(received.values.count / 1024 / httpJourneys) : null,
      rate_limited: limited ? limited.values.count : 0,
      ws: {
        subscribed: wsSub ? wsSub.values.count : 0, notifications: wsNotif ? wsNotif.values.count : 0,
        errors: wsErr ? wsErr.values.count : 0,
      },
    },
    capacity,
  });
  const text = `${lines.join('\n')}\n\n===K6_SUMMARY_JSON_BEGIN===\n${JSON.stringify(summaryData)}\n===K6_SUMMARY_JSON_END===\n` +
    `===K6_JOURNEYS_JSON_BEGIN===\n${journeysBlock}\n===K6_JOURNEYS_JSON_END===\n`;
  return { stdout: text };
}
