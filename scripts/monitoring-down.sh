#!/usr/bin/env bash
# Phase 10c: tear the monitoring stack down and reclaim its RAM (~1.5-2 GiB actual, ~3 GiB of
# limits). Safe by design: Loki/Tempo/Prometheus storage is deliberately disposable (emptyDir,
# short retention — benchmark evidence lives in thesis/, never only in the TSDB), so a later
# `make mon-up` recreates everything from the pinned charts + committed values/dashboards.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env
require_cmd helm
require_cmd kubectl

MON_NS="$MONITORING_NAMESPACE"

if ! kubectl get namespace "$MON_NS" >/dev/null 2>&1; then
  ok "Namespace $MON_NS does not exist — nothing to tear down."
  exit 0
fi

step "Uninstalling Helm releases in $MON_NS"
for release in postgres-exporter tempo alloy loki monitoring; do
  if helm status "$release" -n "$MON_NS" >/dev/null 2>&1; then
    helm uninstall "$release" -n "$MON_NS" --wait --timeout 120s >/dev/null \
      && ok "$release uninstalled" || warn "$release uninstall did not finish cleanly"
  else
    info "$release: not installed — skipped"
  fi
done

step "Deleting namespace $MON_NS (removes dashboards/rules/monitors/secrets/ingress)"
kubectl delete namespace "$MON_NS" --timeout=180s >/dev/null && ok "namespace deleted" \
  || warn "namespace deletion timed out — check 'kubectl get ns $MON_NS -o yaml' for finalizers"

# kube-prometheus-stack leaves its CRDs behind on purpose (uninstall never deletes CRDs).
# Keep them: they are cluster-scoped, cost nothing, and deleting them would cascade-delete any
# ServiceMonitor/Rule objects on a re-install race. `make mk-down ARGS=--delete` wipes everything.
info "Prometheus-operator CRDs are kept (harmless; removed with the cluster itself)."
