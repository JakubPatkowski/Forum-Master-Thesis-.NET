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
//   J4 = thread WITH a photo (lib/assets.js makePng, J4_IMAGE_PX; A: presigned path + attach, B: ingest + 202/poll);
//   J9 = direct message (A: open-or-get conversation once per VU, then send; B: POST /messages/{friend}).
export function wsHold(baseUrl, data, session, holdMs, counters, pick, probe);
//   counters = { subscribed, notifications, errors, probesSent, probesReceived, probesLost, probesUnresolved,
//   receipt } (k6 metrics owned by main.js); probe = { intervalMs, timeoutMs } | null — every intervalMs the holding
//   VU writes ONE comment and times the write request's start until the push carrying that comment id reaches its
//   own socket (forum_ws_receipt); unanswered after timeoutMs = lost. Returns the ws.connect response or null.
```

Variants every adapter honours (harness h4): `AUTH_READS=1` — read journeys carry the session (A bearer, B
cookie; deviation D3's signed-in browsing); A additionally re-logs a VU in on a 401 (expired 15-min access
token; `forum_relogins`), B's DB sessions live 30 days.

Mix `v2` / harness `h4-2026-10-18`: J4 (3 %) and J9 (4 %) have weight on both targets, J8 holds carry receipt
probes, and the seeders' word bank is the 300-word Zipf bank (`lib/assets.js` SEARCH_WORDS — the same list as
A's `SeedText.cs` and B's `seedbench.go`). v2 numbers are not comparable to v1.

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
