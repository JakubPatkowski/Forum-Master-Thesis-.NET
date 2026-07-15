# k8s/monitoring — the Phase 10c monitoring stack, as code

Everything the `monitoring` namespace runs is defined here and applied by
`scripts/monitoring-up.sh` (`make mon-up`). **Helm is used for this directory ONLY** — the rest
of the cluster stays hand-rolled YAML (recorded decision, `infrastructure/monitoring/README.md`).

| File | What |
|---|---|
| `values-kube-prometheus-stack.yaml` | Prometheus (6h/2GB retention, exemplar-storage), Grafana (datasources incl. Loki `@tr` derived field + Tempo `tracesToLogsV2`), node-exporter, kube-state-metrics, operator. Alertmanager disabled (recorded). |
| `values-loki.yaml` | SingleBinary, filesystem, 24h retention, ingestion caps, emptyDir at `/var/loki`, no gateway/canary/caches |
| `values-alloy.yaml` | DaemonSet log shipper; tails ALL pods via the kubelet API (no hostPath), ships lines whole |
| `values-tempo.yaml` | single binary, OTLP gRPC :4317 ← backend, 24h retention. From **grafana-community** (the grafana/tempo chart is deprecated — repo migration) |
| `values-postgres-exporter.yaml` | creds mirrored from the app ns secret; `fullnameOverride` so job=`postgres-exporter` |
| `servicemonitors.yaml` | backend (`app: backend`, port `http`), rabbitmq (port `prometheus`), minio (port `s3`, `/minio/v2/metrics/cluster`) |
| `prometheus-rules.yaml` | 15 alerts: the plan's 13 + `BackgroundLoopStale` + `UnhandledErrorsPresent` (OBSERVABILITY-CONTRACT.md) |
| `ingress-grafana.yaml` | `https://grafana.forum.local` (cert SAN minted in 10b; secret copied into this ns by mon-up) |
| `grafana-dashboards/*.json` | 8 dashboards, plain JSON (UI-importable); mon-up wraps them into ConfigMaps labeled `grafana_dashboard=1` |
| `QUERIES.md` | generated, greppable index of every dashboard query (for 9c's bench-run.sh) — regenerate with `extract-queries.py` |

## Version pinning

Chart versions live in `scripts/lib.sh` defaults (override in `.env`) and each values file's
header comment states the chart version it was tested against. **Never install unpinned**
(`helm search repo <chart>` to see what's current before bumping; after a bump, `helm template`
against the new version — key layouts drift between chart majors).

## Operations

```bash
make mon-up      # idempotent install/upgrade of all five releases + monitors/rules/dashboards/ingress
make mon-check   # asserts: all scrape targets UP, Loki ingesting backend lines, Tempo ready
make mon-down    # uninstall + delete the namespace (storage is disposable by design)
make tunnels     # adds grafana -> localhost:13001, prometheus -> localhost:19090 when the ns exists
```

Gotchas that already bit once (don't rediscover them):

- **PSS**: this namespace is `enforce=privileged` (NOT the plan's original `baseline` — baseline
  forbids hostNetwork/hostPath, which node-exporter requires; pods would be rejected at
  admission). `warn/audit=baseline` keeps drift visible. The app namespace stays `restricted`.
- **ServiceMonitor silently ignored** = missing `release: monitoring` label (kept on all our
  objects) or a Service without the `app` label / named port (pinned in 10b).
- **Backend scrape needs `honorLabels: true`**: the operator attaches `service=<Service name>` as a
  target label, which collides with the Forum Meter's own `service` tag
  (`forum_hosted_service_tick_age_seconds`) — without it the loop names become `exported_service`,
  the dead-loop panel flattens to one series and BackgroundLoopStale false-fires on the sweeper.
- **Loki + `persistence: false`** mounts nothing at `/var/loki` → crashloop on the read-only
  rootfs; the values file adds an explicit emptyDir.
- Dashboard edits: edit JSON here (or export from the UI), then `make mon-up` re-applies the
  ConfigMaps and `python3 k8s/monitoring/extract-queries.py` refreshes QUERIES.md.
- Metric names in panels are the **live-verified** ones — .NET runtime metrics are `dotnet_*`
  (not the plan's `process_runtime_dotnet_*`); check `/metrics` via `localhost:18080` before
  "fixing" a panel query.
