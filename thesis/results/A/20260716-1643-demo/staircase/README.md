# HPA staircase artifact (clean metric pool)

A dedicated `demo` run captured AFTER fixing the Job-pod/HPA-selector collision (see the plan §9c
callout + `k8s/backend/*-job.yaml` `ttlSecondsAfterFinished`). With no Completed `app=backend` Job
pod polluting the HPA metric pool, the autoscaler steps 1→2→3 tracking the ramp with no intervention:

- 1/1 idle → 1/2 at ~50 s into the ramp (40 VU) → 2/2
- 2/3 during the 80 VU plateau → 3/3 (capped at HPA max)

54,760 reqs · 103.8 rps · p95 170 ms · p99 477 ms · 0.00% failed. `samples-*.json` has the per-5s
HPA current/desired + summed backend CPU/mem; `summary-*.json` the k6 per-endpoint trends.

This complements the statistical archive (../run-1..3, 3 repeats, mean±stddev) which ran with a
lingering reseed Job pod and therefore scaled only 1→2 — the bug this run's fix resolves.
