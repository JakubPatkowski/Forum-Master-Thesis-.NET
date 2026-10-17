// Shared check/429 accounting for both target adapters (AB-ALIGNMENT-INSTRUCTIONS-A.md A-01).
// A 429 anywhere means the limiter raise (A) / GOMX_AUTH_RATELIMIT=0 (B) did not apply — the run is
// measuring a rate limiter, not the application, and must fail (main.js thresholds count<1).

import { check } from 'k6';
import { Counter } from 'k6/metrics';

export const rateLimited = new Counter('forum_rate_limited');

/** Records the check + counts 429s. Returns true when the status is one of okStatuses. */
export function track(res, name, okStatuses = [200]) {
  if (res.status === 429) {
    rateLimited.add(1);
  }
  const ok = okStatuses.includes(res.status);
  check(res, { [`${name} ok`]: () => ok });
  return ok;
}
