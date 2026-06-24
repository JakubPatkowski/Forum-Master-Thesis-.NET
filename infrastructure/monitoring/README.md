# Monitoring stack

kube-prometheus-stack (Prometheus + Grafana) + Loki (logs) + Tempo (traces).
Backend exposes /metrics (Prometheus) and exports OTLP traces to Tempo via the collector.
Dashboards: resource usage, HTTP latency (p50/p95/p99), HPA replicas — feed the thesis benchmarks.
