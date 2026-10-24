// Shared check/429 accounting for both target adapters (AB-ALIGNMENT-INSTRUCTIONS-A.md A-01).
// A 429 anywhere means the limiter raise (A) / GOMX_AUTH_RATELIMIT=0 (B) did not apply — the run is
// measuring a rate limiter, not the application, and must fail (main.js thresholds count<1).
//
// Harness h6: every tracked outcome is ALSO counted per endpoint (forum_check_ok / forum_check_failed{endpoint}) so
// the summary can say how many comment_create / thread_create / dm_send calls were really accepted — k6's own
// http_req_failed sees only status ≥ 400, while B answers a rejected write with a 303 and the adapters turn such
// rejections into synthetic 598/599 statuses that never reach http_req_failed. ab-round.sh compares these accepted
// counts with the dataset delta (rows that really appeared) to catch "acknowledged but not persisted".

import { check } from 'k6';
import { Counter } from 'k6/metrics';

export const rateLimited = new Counter('forum_rate_limited');
export const checkOk = new Counter('forum_check_ok');
export const checkFailed = new Counter('forum_check_failed');

/** Records the check + counts 429s. Returns true when the status is one of okStatuses. */
export function track(res, name, okStatuses = [200]) {
  if (res.status === 429) {
    rateLimited.add(1);
  }
  const ok = okStatuses.includes(res.status);
  check(res, { [`${name} ok`]: () => ok });
  (ok ? checkOk : checkFailed).add(1, { endpoint: name });
  return ok;
}
