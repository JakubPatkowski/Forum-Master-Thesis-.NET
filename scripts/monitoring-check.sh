#!/usr/bin/env bash
# Phase 10c: assert the monitoring stack actually works (`make mon-check`). Humans forget to open
# the Targets page — this scriptifies the check the plan's Watch-out list demands:
#   1. every expected Prometheus scrape job is UP (backend, rabbitmq, minio, postgres-exporter,
#      kubelet/cAdvisor, node-exporter, kube-state-metrics),
#   2. Loki has recent backend log lines (Alloy -> Loki pipeline alive),
#   3. Tempo is ready (backend's OTLP endpoint has somewhere to send).
# Talks to the services over TEMPORARY port-forwards on 29090/23100/23200 — deliberately NOT the
# dev-tunnels ports (19090/...) so a running `make tunnels` never collides with this script.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env
require_cmd kubectl
require_cmd jq "apt-get install -y jq"

MON_NS="$MONITORING_NAMESPACE"
PROM_PORT=29090 LOKI_PORT=23100 TEMPO_PORT=23200
PIDS=()
cleanup() { for p in "${PIDS[@]}"; do kill "$p" 2>/dev/null || true; done; wait 2>/dev/null || true; }
trap cleanup EXIT

kubectl get namespace "$MON_NS" >/dev/null 2>&1 || die "Namespace $MON_NS missing — run 'make mon-up' first."

step "Port-forwarding prometheus/loki/tempo (temporary)"
kubectl -n "$MON_NS" port-forward svc/monitoring-kube-prometheus-prometheus "$PROM_PORT:9090" >/dev/null 2>&1 & PIDS+=($!)
kubectl -n "$MON_NS" port-forward svc/loki "$LOKI_PORT:3100" >/dev/null 2>&1 & PIDS+=($!)
kubectl -n "$MON_NS" port-forward svc/tempo "$TEMPO_PORT:3200" >/dev/null 2>&1 & PIDS+=($!)
sleep 3

# --- 1. Prometheus targets ----------------------------------------------------
# Waits up to ~2 min: after mon-up Prometheus needs a config-reload cycle before targets appear.
step "Prometheus scrape targets"
EXPECTED_JOBS=(backend rabbitmq minio postgres-exporter kubelet node-exporter kube-state-metrics)
FAILED=()
for job in "${EXPECTED_JOBS[@]}"; do
  # job label mapping: backend/rabbitmq/minio = Service names (our ServiceMonitors);
  # postgres-exporter = fullnameOverride; kubelet & friends = kps defaults.
  case "$job" in
    kubelet)            query='max(up{job="kubelet",metrics_path="/metrics/cadvisor"})' ;;
    node-exporter)      query='max(up{job="node-exporter"})' ;;
    kube-state-metrics) query='max(up{job="kube-state-metrics"})' ;;
    *)                  query="max(up{job=\"$job\"})" ;;
  esac
  up=""
  for _ in $(seq 1 24); do
    up="$(curl -fsS --max-time 5 "http://127.0.0.1:$PROM_PORT/api/v1/query" \
            --data-urlencode "query=$query" 2>/dev/null | jq -r '.data.result[0].value[1] // empty')"
    [[ "$up" == "1" ]] && break
    sleep 5
  done
  if [[ "$up" == "1" ]]; then ok "$job UP"; else warn "$job NOT up (query: $query)"; FAILED+=("$job"); fi
done

# --- 2. Loki ingestion ---------------------------------------------------------
step "Loki: recent backend log lines"
lines="$(curl -fsS --max-time 10 -G "http://127.0.0.1:$LOKI_PORT/loki/api/v1/query" \
  --data-urlencode "query=sum(count_over_time({namespace=\"$K8S_NAMESPACE\", app=\"backend\"}[15m]))" 2>/dev/null \
  | jq -r '.data.result[0].value[1] // "0"')"
if [[ "${lines:-0}" -gt 0 ]]; then ok "Loki has $lines backend lines in the last 15m"
else warn "Loki has NO backend lines — check the alloy DaemonSet"; FAILED+=("loki-ingest"); fi

# --- 3. Tempo readiness ----------------------------------------------------------
step "Tempo readiness"
if curl -fsS --max-time 5 "http://127.0.0.1:$TEMPO_PORT/ready" >/dev/null 2>&1; then ok "Tempo ready"
else warn "Tempo /ready failed"; FAILED+=("tempo"); fi

echo
if ((${#FAILED[@]})); then
  die "mon-check FAILED: ${FAILED[*]}"
fi
ok "All monitoring checks passed."
