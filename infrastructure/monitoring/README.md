# Monitoring stack (Phase 10c)

kube-prometheus-stack (Prometheus 3 + Grafana + operator + node-exporter + kube-state-metrics),
**Loki** (logs, SingleBinary/filesystem), **Alloy** (log shipper — Promtail is EOL upstream),
**Tempo** (traces, single binary; the backend exports OTLP gRPC straight to `tempo:4317`, **no
otel-collector** — one less pod, nothing to gain without multi-target pipelines) and
**prometheus-postgres-exporter**. Everything installs via **Helm with pinned versions** from
`k8s/monitoring/` (`make mon-up`); Helm is used for the monitoring stack ONLY — the app stays
hand-rolled YAML (recorded decision).

- Values/monitors/rules/dashboards: [`k8s/monitoring/`](../../k8s/monitoring/) (see its README for operations)
- Metric names, log keys, ready-made queries: [`docs/architecture/OBSERVABILITY-CONTRACT.md`](../../docs/architecture/OBSERVABILITY-CONTRACT.md) — **test-verified, authoritative**
- Alertmanager is **disabled** (recorded §1 decision): rules evaluate and display (Prometheus
  `/alerts`, Grafana Alerting) but nothing dispatches — on a thesis laptop a firing alert means
  "investigate before trusting the benchmark numbers", not "page somebody".

## The correlation story (metric → trace → log → trace)

One user-visible id ties everything together; each hop below is provisioned by
`values-kube-prometheus-stack.yaml` and was round-trip-verified live.

1. **Request → everything.** `CorrelationIdMiddleware` stamps `CorrelationId` on every log line;
   OTel gives the same request a `TraceId`, which Serilog emits as **`@tr`** (compact JSON).
   The response carries both `X-Correlation-ID` and a `correlationId` in every ProblemDetails
   body — including 500s (Phase 9a fixed exactly that path). Bus messages carry the correlation
   id (Phase 6), so consumer-side log lines on other pods correlate too:
   ```logql
   {namespace="forum-dotnet", app="backend"} | json | CorrelationId="<id-from-X-Correlation-ID>"
   ```
2. **Metric → trace (exemplars).** Prometheus runs with `exemplar-storage`; the OTel .NET SDK
   attaches trace-based exemplars to histogram samples. On the App RED latency panels, exemplar
   dots open the exact Tempo trace behind a p99 spike (`exemplarTraceIdDestinations` on the
   Prometheus datasource).
3. **Trace → logs.** Tempo datasource `tracesToLogsV2` jumps to the emitting pod's Loki stream
   ±5 s around the span.
4. **Log → trace.** Loki derived field matching `"@tr":"(\w+)"`.
   ⚠ The plan's original regex (`"TraceId":"…"`) never matches — the compact-JSON key is `@tr`
   (OBSERVABILITY-CONTRACT.md §1). After `| json`, `@`-keys become `_`-prefixed fields (`_tr`).

**Verification recipe** (used in the 10c DoD): `curl -si https://forum.local/api/...`, take
`X-Correlation-ID`, find the line in Grafana Explore (Loki), click its TraceID derived field →
Tempo span tree (HTTP → Npgsql), then "Logs for this span" → back to the same line.

## Dashboards (Grafana folder "Forum")

| Dashboard | What it answers |
|---|---|
| Cluster Overview | is everything green (status row), node/pod resources, restarts, PVCs, monitoring-ns budget |
| App RED (backend) | request rate / error % / p50-p95-p99 (with exemplars), per-route table, .NET runtime |
| **Errors & Problems** | scrollable recent-error log table (Loki), unhandled errors by category, 401/403 (attack-shaped) vs 422 (frontend-bug-shaped) rejection split, failing routes, **background-loop tick age** (the silently-dead-loop detector `/health/ready` can't see), poison trend |
| Database | connections vs the 100 cap (pool math 3×30), per-state, TPS, cache hit, locks/deadlocks, DB size |
| RabbitMQ | per-queue depth/consumers/unacked, publish/deliver/ack, poison stat |
| MinIO | bucket size/objects over a run, S3 rate by API, errors, TTFB p95 |
| HPA Scaling | replicas current/desired/max with scale-event annotations, the 70% CPU driving signal |
| Business Metrics | auth outcomes, content/reaction rates, outbox lag p95 + publish failures, consumer outcomes, WebSocket gauges |

Every query is greppable in [`k8s/monitoring/QUERIES.md`](../../k8s/monitoring/QUERIES.md)
(generated from the dashboard JSONs — the JSON `expr` fields are the single source of truth;
Phase 9c's `bench-run.sh` reuses them against the Prometheus range API).

## Access

- `https://grafana.forum.local` (admin/admin) through the ingress (`make tunnels` + Windows hosts
  entry — same mechanics as the app, runbook §4b), or `http://localhost:13001` via the tunnel.
- Prometheus: `http://localhost:19090` via `make tunnels` (`/targets`, `/rules`, `/alerts`).
- `make mon-check` asserts every scrape target UP + Loki ingesting + Tempo ready.
