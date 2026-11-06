# Notes on the v3 figures (read with `README.md`)

Round `thesis/results/rounds/20261024-2150-v3/` (demo, N = 10 per side; capacity invalid — no capacity figures).
Analysis and caveats: `thesis/results/campaign-20261024-2129/05-analysis/CAMPAIGN-REPORT.md`, digest
`…/05-analysis/ARTICLE-RESULTS-DIGEST.md`.

- **Rendering:** produced with a scratch copy of `scripts/thesis_figures.py` whose only change is the sample size in three
  titles/captions (the tracked script hard-codes "N = 5"); patch:
  `thesis/results/campaign-20261024-2129/05-analysis/thesis_figures-N-label-fix.patch`. Data, axes and layout are the
  script's.
- **`latency-per-journey-demo`:** the v3 latencies come from a host saturated by the load generator (k6 up to 5.5 of 8 cores,
  caused by the per-journey PNG synthesis of J4). Do not use this figure as an H1 latency result; if shown, caption it as
  "latency under load-generator contention". J4 bars include ≈ 0.94 s of k6-side PNG synthesis on both sides.
- **`per-repeat-values-demo`:** the generated caption's last sentence ("…a gdzie przeplatają (CPU aplikacji)") is v1 text. In v3
  every panel separates, application CPU included (MW p < 0.001, δ = −0.88). The p95 panel is contaminated as above.
- **`cpu-per-journey-by-layer-demo`, `memory-by-layer-demo`:** the usable H1 figures (cgroup accounting); memory carries D8
  (platform-default GC policies).
- **`lighthouse-cluster`:** rendered from `thesis/results/campaign-20261024-2129/04-static/lighthouse/` (v3 quiet-host pass,
  2026-10-25 03:03–03:10 CET). The generated caption's last sentence ("Przebieg walidacyjny (do powtórzenia na nieobciążonej
  maszynie)") refers to the 2026-10-18 run and does not apply to these numbers.
