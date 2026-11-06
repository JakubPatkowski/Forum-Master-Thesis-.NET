// Campaign v3 live check: B must NOT push a private-community comment to an anonymous or non-member socket
// (thesis-v3-b `broadcastComment` gate), while a member socket receives it; a public comment reaches everyone
// (positive control that the anonymous socket works at all).
// env: BASE (https://gomx.local), IP (ingress), MEMBER (username, active member of the private space), NONMEMBER,
//      PRIV_POST (post id in that private space), PUB_POST (public post id), OUT (summary json path)
import http from 'k6/http';
import ws from 'k6/ws';
import { Counter } from 'k6/metrics';
import { sleep } from 'k6';

const BASE = __ENV.BASE || 'https://gomx.local';
const WSURL = BASE.replace(/^http/, 'ws');
const MARK = `zzgate${Date.now()}`;
const PRIV_MARK = `${MARK}priv`;
const PUB_MARK = `${MARK}pub`;
const hits = {};
for (const who of ['anon', 'nonmember', 'member']) {
  hits[`${who}_priv`] = new Counter(`${who}_priv_hits`);
  hits[`${who}_pub`] = new Counter(`${who}_pub_hits`);
  hits[`${who}_frames`] = new Counter(`${who}_frames`);
}
const opened = new Counter('sockets_opened');

export const options = {
  hosts: __ENV.IP ? { 'gomx.local': __ENV.IP } : {},
  scenarios: {
    anon: { executor: 'per-vu-iterations', vus: 1, iterations: 1, exec: 'anon' },
    nonmember: { executor: 'per-vu-iterations', vus: 1, iterations: 1, exec: 'nonmember' },
    member: { executor: 'per-vu-iterations', vus: 1, iterations: 1, exec: 'member' },
    poster: { executor: 'per-vu-iterations', vus: 1, iterations: 1, exec: 'poster', startTime: '5s' },
  },
};

function login(username) {
  const res = http.post(`${BASE}/login`, { username, password: 'DemoPassword1' }, { redirects: 0, jar: new http.CookieJar() });
  const c = res.cookies && res.cookies.forum_session;
  if (res.status !== 303 || !c || !c[0].value) throw new Error(`login ${username} failed: ${res.status}`);
  return `forum_session=${c[0].value}`;
}

function hold(who, cookie) {
  const headers = cookie ? { Cookie: cookie } : {};
  const res = ws.connect(`${WSURL}/ws`, { headers }, (socket) => {
    socket.on('open', () => opened.add(1, { who }));
    socket.on('message', (m) => {
      hits[`${who}_frames`].add(1);
      if (m.indexOf(PRIV_MARK) >= 0) hits[`${who}_priv`].add(1);
      if (m.indexOf(PUB_MARK) >= 0) hits[`${who}_pub`].add(1);
    });
    socket.setTimeout(() => socket.close(), 15000);
  });
  if (!res || res.status !== 101) throw new Error(`${who} ws status ${res && res.status}`);
}

export function anon() { hold('anon', null); }
export function nonmember() { hold('nonmember', login(__ENV.NONMEMBER)); }
export function member() { hold('member', login(__ENV.MEMBER)); }
export function poster() {
  const cookie = login(__ENV.MEMBER);
  const p = http.post(`${BASE}/posts/${__ENV.PRIV_POST}/comments`, { body: `private gate check ${PRIV_MARK}` },
    { redirects: 0, jar: new http.CookieJar(), headers: { Cookie: cookie } });
  console.log(`private comment POST status=${p.status} location=${p.headers.Location}`);
  sleep(2);
  const q = http.post(`${BASE}/posts/${__ENV.PUB_POST}/comments`, { body: `public gate check ${PUB_MARK}` },
    { redirects: 0, jar: new http.CookieJar(), headers: { Cookie: cookie } });
  console.log(`public comment POST status=${q.status} location=${q.headers.Location}`);
}

export function handleSummary(data) {
  const v = (n) => (data.metrics[n] ? data.metrics[n].values.count : 0);
  const out = { marker: MARK, sockets_opened: v('sockets_opened') };
  for (const who of ['anon', 'nonmember', 'member']) {
    out[who] = { frames: v(`${who}_frames`), private_comment_frames: v(`${who}_priv_hits`), public_comment_frames: v(`${who}_pub_hits`) };
  }
  out.verdict = out.anon.private_comment_frames === 0 && out.nonmember.private_comment_frames === 0 &&
    out.member.private_comment_frames > 0 && out.anon.public_comment_frames > 0 ? 'PASS' : 'CHECK';
  return { [__ENV.OUT || 'b-ws-private-gate.json']: JSON.stringify(out, null, 2), stdout: JSON.stringify(out) + '\n' };
}
