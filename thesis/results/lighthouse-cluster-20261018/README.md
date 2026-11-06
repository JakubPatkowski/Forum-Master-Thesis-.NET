# Lighthouse on the benchmark deployment — tooling validation run (2026-10-18 ~23:10–23:30)

`scripts/lighthouse.sh --cluster --target a|b --preset mobile|desktop` (harness h4): the same two pages per
architecture (largest public space's feed + its first thread) through the ingress (`https://forum.local`,
`https://gomx.local`), one Lighthouse version (lhci via npx, Chrome for Testing 151 headless shell), 3 runs
per page, representative (median) run reported. `mobile` = Lighthouse's default form factor (Moto G Power
class screen, simulated slow-4G, 4× CPU slowdown) — identical to B's own `lighthouserc.json`; `desktop` = the
built-in desktop preset.

**Conditions of THIS run (validation, not the final numbers):** A's web image was the pre-fix build
(`git-57f63d0-dirty`; the audited pages are unaffected by the home-feed bug), both datasets were the v1 seed
plus the writes of the h4 smoke runs, and a Playwright agent was building/serving the SPA locally on the
same host at the time (CPU contention inflates TBT/TTI on both sides). Re-run on a quiet box after the image
rebuild for the thesis tables (`AB-START-HERE.md` §8.1 step 6).

| page | preset | A perf | B perf | A LCP ms | B LCP ms | A TBT ms | B TBT ms | A CLS | B CLS | A a11y | B a11y |
|---|---|---|---|---|---|---|---|---|---|---|---|
| space feed | mobile | 0.56 | 0.89 | 2 834 | 2 221 | 776 | 34 | 0.237 | 0.195 | 0.91 | 0.88 |
| thread | mobile | 0.85 | 0.94 | 2 742 | 2 028 | 442 | 13 | 0.000 | 0.139 | 0.95 | 0.94 |
| space feed | desktop | 0.97 | 1.00 | 597 | 482 | 109 | 0 | 0.078 | 0.018 | 0.95 | 0.89 |
| thread | desktop | 0.99 | 1.00 | 651 | 442 | 68 | 0 | 0.001 | 0.001 | 1.00 | 0.94 |

Reading: on the emulated mid-range phone the SPA pays its hydration/JS cost in Total Blocking Time (A 776 ms
vs B 34 ms on the feed), which is what separates the performance scores; on desktop both are near the top of
the scale. B's CLS on the feed/thread (0.14–0.20) and its accessibility score (the `meta-viewport`
`user-scalable=no` audit) are B-side findings; A's feed CLS (0.24 mobile) is an A-side finding. Raw LHR JSON
per run is in the subfolders.
