// Wire conventions shared by BOTH target adapters (A/B harness v1, 2026-10-17).
//
// Accept-Encoding: k6 sends NO Accept-Encoding header by default (verified on k6 v2.1.0 against an echo server),
// so without it neither architecture is ever compressed — while every real browser asks for gzip. Both ingress
// hosts sit behind the same ingress-nginx with the cluster-wide gzip ConfigMap (setup-minikube.sh), and B's
// in-process gzip is off in its bench overlay (GOMX_COMPRESS=0), so this header makes compression identical and
// realistic on both sides. k6 transparently decompresses gzip bodies before res.body / res.json() see them.
export const BROWSER_HEADERS = Object.freeze({ 'Accept-Encoding': 'gzip' });

// Bodies a journey never parses are discarded: k6 still downloads every byte (timings include the full
// transfer), it just does not buffer/convert them — which keeps the load generator off the critical path for B's
// 175–750 KB HTML pages and A's comment trees. Bodies that ARE parsed (feed ids, sentinel cursor, comment ids,
// login cookies/tokens, WS tickets) keep the default 'text'.
export const DISCARD = Object.freeze({ responseType: 'none' });

/** Merges the browser headers into a k6 params object (extra headers win). */
export function withBrowserHeaders(params = {}) {
  return { ...params, headers: { ...BROWSER_HEADERS, ...(params.headers || {}) } };
}
