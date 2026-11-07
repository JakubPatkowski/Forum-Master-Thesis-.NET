# Post-hoc evidence for this round (added 2026-11-07, after the round)

`host-memory-prometheus-20261107-0625-0645.json` — Prometheus series exported at 12:55 CET, minutes before the
6-hour retention dropped them. They cover only 06:25–06:45 CET, i.e. `capacity/A/run-2` (scenario start
06:30:03). Queries are stored next to each result.

What they show: the WSL host (11 966 MiB RAM, 4 096 MiB swap) already had ~3 650 MiB of swap in use before the run;
swap-out started at step c03 (100 offered journeys/s, 06:32), exactly when A's core p95 jumped to 640–940 ms
while app and DB CPU were far from their limits; by c09–c11 swap was 100 % full and MemAvailable fell to
548 MiB (k6 grew to ~4.3 GiB RSS with up to 300 VUs). The automatic validity rules (restarts, failures, 429,
generator CPU) do not look at host memory, so the run stayed "valid".

Consequence: the laptop capacity numbers are preliminary; A's knee at 100 j/s in run 2 is a host artefact.
Nothing in this directory is read by `scripts/ab_report.py` (it only globs `*/*/run-*/run.json`).
