#!/usr/bin/env bash
# Phase 10c: install/upgrade the full monitoring stack (idempotent — `helm upgrade --install`
# everywhere, `kubectl apply` for our own objects). Wrapped by `make mon-up`.
#
#   kube-prometheus-stack  (Prometheus + Grafana + operator + node-exporter + kube-state-metrics)
#   loki                   (logs, SingleBinary + filesystem, 24h retention)
#   alloy                  (log shipper DaemonSet — tails pods via the kubelet API, no hostPath)
#   tempo                  (traces, single binary, OTLP gRPC :4317 <- backend's Otlp__Endpoint)
#   postgres-exporter      (the database dashboard is impossible without it)
#
# plus our ServiceMonitors, PrometheusRule, Grafana dashboards (ConfigMaps generated from the
# JSON files in k8s/monitoring/grafana-dashboards/) and the Grafana ingress.
#
# Chart versions are PINNED via lib.sh defaults / .env overrides — never installed unpinned.
# Run `scripts/monitoring-check.sh` (make mon-check) afterwards; Prometheus needs ~1 min before
# targets appear.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env
require_cmd helm "https://helm.sh/docs/intro/install/"
require_cmd kubectl
kubectl version >/dev/null 2>&1 || die "No reachable cluster (make mk-up first)."

MON_NS="$MONITORING_NAMESPACE"
MON_DIR="$REPO_ROOT/k8s/monitoring"

step "Helm repositories"
helm repo add prometheus-community https://prometheus-community.github.io/helm-charts >/dev/null
helm repo add grafana https://grafana.github.io/helm-charts >/dev/null
# The grafana/tempo chart is deprecated (repo migration) — tempo installs from grafana-community.
helm repo add grafana-community https://grafana-community.github.io/helm-charts >/dev/null
helm repo update prometheus-community grafana grafana-community >/dev/null
ok "repos ready (kps=$KPS_VERSION loki=$LOKI_VERSION alloy=$ALLOY_VERSION tempo=$TEMPO_VERSION pgexp=$PGEXP_VERSION)"

step "Namespace $MON_NS (PSS: enforce=privileged, warn/audit=baseline)"
# PSS CORRECTION (recorded): the plan said `baseline`, but baseline FORBIDS hostNetwork/hostPID
# and hostPath volumes — node-exporter needs all three, so enforce=baseline would reject its pods
# at admission. `privileged` (= unrestricted) is the standard posture for a node-exporter-bearing
# monitoring namespace; warn/audit stay at baseline so drift in everything else remains visible.
# The app namespace keeps enforce=restricted — this exception is monitoring-only.
kubectl create namespace "$MON_NS" --dry-run=client -o yaml | kubectl apply -f - >/dev/null
kubectl label namespace "$MON_NS" \
  pod-security.kubernetes.io/enforce=privileged \
  pod-security.kubernetes.io/warn=baseline \
  pod-security.kubernetes.io/audit=baseline --overwrite >/dev/null
ok "namespace labeled"

step "Secrets in $MON_NS"
# postgres-exporter credentials: secrets are namespace-scoped, so mirror the app namespace's
# postgres-credentials into monitoring (values-postgres-exporter.yaml reads user+password from it).
if kc get secret postgres-credentials >/dev/null 2>&1; then
  kc get secret postgres-credentials -o yaml \
    | sed -e "s/namespace: $K8S_NAMESPACE/namespace: $MON_NS/" \
          -e '/resourceVersion:/d' -e '/uid:/d' -e '/creationTimestamp:/d' \
    | kubectl apply -f - >/dev/null
  ok "postgres-credentials mirrored from $K8S_NAMESPACE"
else
  warn "postgres-credentials not found in $K8S_NAMESPACE — postgres-exporter will CrashLoop until 'make mk-deploy' has run"
fi
# Grafana ingress TLS: same cert as the app (SAN grafana.forum.local minted in 10b).
if [[ -f "$REPO_ROOT/k8s/ingress/tls/tls.crt" ]]; then
  kubectl -n "$MON_NS" create secret tls forum-tls \
    --cert="$REPO_ROOT/k8s/ingress/tls/tls.crt" --key="$REPO_ROOT/k8s/ingress/tls/tls.key" \
    --dry-run=client -o yaml | kubectl apply -f - >/dev/null
  ok "forum-tls created/updated in $MON_NS"
else
  warn "k8s/ingress/tls/tls.crt missing (run 'make mk-tls') — https://grafana.$INGRESS_HOST will not work"
fi

step "kube-prometheus-stack $KPS_VERSION (release: monitoring)"
helm upgrade --install monitoring prometheus-community/kube-prometheus-stack \
  -n "$MON_NS" -f "$MON_DIR/values-kube-prometheus-stack.yaml" --version "$KPS_VERSION" >/dev/null
ok "applied"

step "loki $LOKI_VERSION / alloy $ALLOY_VERSION / tempo $TEMPO_VERSION / postgres-exporter $PGEXP_VERSION"
helm upgrade --install loki grafana/loki \
  -n "$MON_NS" -f "$MON_DIR/values-loki.yaml" --version "$LOKI_VERSION" >/dev/null
helm upgrade --install alloy grafana/alloy \
  -n "$MON_NS" -f "$MON_DIR/values-alloy.yaml" --version "$ALLOY_VERSION" >/dev/null
helm upgrade --install tempo grafana-community/tempo \
  -n "$MON_NS" -f "$MON_DIR/values-tempo.yaml" --version "$TEMPO_VERSION" >/dev/null
helm upgrade --install postgres-exporter prometheus-community/prometheus-postgres-exporter \
  -n "$MON_NS" -f "$MON_DIR/values-postgres-exporter.yaml" --version "$PGEXP_VERSION" >/dev/null
ok "applied"

step "ServiceMonitors + PrometheusRule + Grafana ingress"
kubectl apply -f "$MON_DIR/servicemonitors.yaml" -f "$MON_DIR/prometheus-rules.yaml" \
              -f "$MON_DIR/ingress-grafana.yaml" >/dev/null
ok "applied"

step "Grafana dashboards (ConfigMaps from k8s/monitoring/grafana-dashboards/*.json)"
# Dashboards are kept as PLAIN JSON (editable, UI-importable, greppable by 9c's bench-run.sh) and
# wrapped into ConfigMaps here. Labels/annotations: grafana_dashboard=1 -> the kps sidecar imports
# them; grafana_folder=Forum -> they land in one folder instead of General.
for f in "$MON_DIR"/grafana-dashboards/*.json; do
  name="dashboard-$(basename "$f" .json)"
  kubectl -n "$MON_NS" create configmap "$name" --from-file="$f" --dry-run=client -o yaml \
    | kubectl label --local -f - grafana_dashboard=1 --dry-run=client -o yaml \
    | kubectl annotate --local -f - grafana_folder=Forum --dry-run=client -o yaml \
    | kubectl apply -f - >/dev/null
  info "$name"
done
ok "dashboards applied"

step "Waiting for the stack to come up (first pull can take minutes)"
kubectl -n "$MON_NS" rollout status deployment/monitoring-kube-prometheus-operator --timeout=300s >/dev/null
kubectl -n "$MON_NS" rollout status deployment/monitoring-grafana --timeout=600s >/dev/null
kubectl -n "$MON_NS" rollout status statefulset/prometheus-monitoring-kube-prometheus-prometheus --timeout=600s >/dev/null \
  || warn "prometheus statefulset not ready yet — give it a minute, then 'make mon-check'"
ok "core components rolled out"

cat <<EOF

${_C_BOLD}Monitoring stack is up.${_C_RESET}
  Grafana     https://grafana.$INGRESS_HOST   (admin/admin; Windows: hosts entry + make tunnels)
              or  make tunnels -> http://localhost:13001
  Prometheus  make tunnels -> http://localhost:19090   (targets: /targets, rules: /rules)
  Next        make mon-check   (asserts every scrape target is UP — Prometheus needs ~1 min)
EOF
