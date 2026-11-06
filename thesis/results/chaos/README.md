# Fault-injection runs (`scripts/ab-chaos.sh`, harness h4)

One architecture at a time under the **demo** load (closed model, 0 → 10 → 40 → 80 VUs over 7 min, 20 WS holds
with receipt probes), reseeded + fresh pods, bench profile applied; faults injected by plain `kubectl` at fixed
offsets after the k6 start, actual timestamps in `faults.json`. `ab_chaos_report.py` buckets the k6 point
stream (`k6-points-*.csv.gz`, http scenario only) per 5 s: failed requests attributable to each fault (until
three clean buckets), recovery time (first of three consecutive buckets with no failures and p95 ≤ 2 × the
pre-fault baseline p95), the maximum p95 after the fault, plus the run's WS errors and probe loss.

| run | fault | failed requests | recovery | max p95 after | notes |
|---|---|---|---|---|---|
| `20261018-2325-A-both-validate` | pod-crash (`--grace-period=0 --force`, 1 of 2 app pods) | **0** | 18 s | 439 ms | the ingress stopped routing to the dead endpoint before any request failed; the replacement pod was Ready within the recovery window |
| `20261018-2325-A-both-validate` | db-restart (`postgres-0` deleted, StatefulSet recreates it) | **4 991** (of 58 117) | 82 s | 7.5 s | the readiness probe gates on Postgres → both app pods unready → ingress 503s until the pool reconnects; WS errors 20 (sockets lived on the killed pod), probe loss 15 % (30 of 200), receipt p95 2.4 s |

| `20261018-2337-B-both-validate` | pod-crash (1 of 2 app pods) | **0** | 9 s | 387 ms | same mechanism; B's replacement pod is Ready faster (no .NET start-up/JIT) |
| `20261018-2337-B-both-validate` | db-restart | **2 373** (of 36 143) | 71 s | 1.5 s | `/readyz` pings Postgres → pods unready → 503s until pgxpool reconnects; WS errors 20, probe loss 13.5 % (27 of 200), receipt p95 173 ms |

Side by side (one repeat each, so directions only): both architectures survive a pod crash without a failed
request; a database restart costs A ~82 s and ~5 000 failed requests versus B ~71 s and ~2 400 (B served
fewer requests per second in this run — compare failure *percentages*: 8.6 % vs 6.6 % of the run). Probe loss
was similar (15 % vs 13.5 %) because both sides lose the sockets that lived on the killed pod; the
received/late/lost split added to the harness right after these runs will show whether A's outbox turns part of
that loss into *late* delivery (its relay replays rows after the DB is back) while B's Redis pub/sub cannot.

These are **tooling-validation runs** (one repeat each, k6 offsets 150 s / 330 s). For the thesis, repeat each
fault ≥ 3× per side (`make ab-chaos ARGS="--target a --fault pod-crash"` etc.) and report the per-fault rows
with their spread; the probe-loss column is the realtime-durability comparison (A's outbox replays a push once
its relay is back, B's Redis pub/sub cannot).
