#!/usr/bin/env bash
# Admin/dev tunnels (Phase 10b): one command opens `kubectl port-forward` to every admin-facing
# service, so WINDOWS tools (browser, DataGrip) reach the cluster via WSL2's built-in localhost
# forwarding — a process listening on localhost inside WSL2 is transparently reachable as
# localhost:<port> from Windows (verified on this machine; requires `.wslconfig` to NOT set
# localhostForwarding=false). Ctrl+C stops every tunnel.
#
# WHY port-forward and not minikube-ip routing: the cluster IP (docker bridge inside the WSL VM)
# is NOT reachable from Windows, and port-forward tunnels through the API server -> kubelet — it
# is architecturally UNAFFECTED by NetworkPolicies, Calico or PSS (different layer than the CNI
# dataplane). Locking the cluster down does not lock the admin out. See
# docs/runbooks/wsl-minikube-setup.md for the full access architecture.
#
# LOCAL PORT RULE: local = remote + 10000 — deliberately NEVER the ports docker-compose publishes
# (5432/5672/15672/9000/9001/8080/3000), so a running compose stack and these tunnels can coexist
# without a tool silently talking to the wrong backend. Getting this wrong with Postgres means
# DataGrip silently browses the COMPOSE database instead of the cluster one.
# EXCEPTION: the ingress tunnel binds REAL 443/80 — the Windows browser resolves forum.local to
# 127.0.0.1 via the hosts file and a hosts file cannot carry a port. Binding <1024 in WSL needs a
# one-time sysctl (instructions printed if missing); without it the ingress tunnel falls back to
# 8443 (fine for curl --resolve smoke tests, NOT for the full browser flow — the SPA's baked API
# origin https://forum.local has no port in it).
#
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env
require_cmd kubectl

# name : namespace : target : localPort : remotePort
TUNNELS=(
  "backend:$K8S_NAMESPACE:svc/backend:18080:80"
  "frontend:$K8S_NAMESPACE:svc/frontend:13000:80"
  "postgres:$K8S_NAMESPACE:svc/postgres:15432:5432"
  "rabbitmq-mgmt:$K8S_NAMESPACE:svc/rabbitmq:25672:15672"
  "minio-console:$K8S_NAMESPACE:svc/minio:19001:9001"
)

# Monitoring tunnels (Phase 10c) — only when the namespace exists, so a cluster without `make
# mon-up` doesn't spawn eternally-reconnecting dead tunnels. Service names VERIFIED against the
# installed charts (release `monitoring`): the Grafana Service is `monitoring-grafana` and
# Prometheus is `monitoring-kube-prometheus-prometheus` — NOT the `kube-prometheus-stack-*` names
# an unreleased chart default would produce. Grafana rides 13001 (13000 is the frontend).
if kubectl get namespace "$MONITORING_NAMESPACE" >/dev/null 2>&1; then
  TUNNELS+=(
    "grafana:$MONITORING_NAMESPACE:svc/monitoring-grafana:13001:80"
    "prometheus:$MONITORING_NAMESPACE:svc/monitoring-kube-prometheus-prometheus:19090:9090"
  )
else
  MONITORING_ABSENT=true
fi

# Ingress: real 443/80 when the kernel allows unprivileged low ports, else 8443 fallback.
UNPRIV_START="$(sysctl -n net.ipv4.ip_unprivileged_port_start 2>/dev/null || echo 1024)"
INGRESS_FALLBACK=false
if [[ "$(id -u)" == "0" || "$UNPRIV_START" -le 80 ]]; then
  TUNNELS+=("ingress-https:ingress-nginx:svc/ingress-nginx-controller:443:443"
            "ingress-http:ingress-nginx:svc/ingress-nginx-controller:80:80")
else
  INGRESS_FALLBACK=true
  TUNNELS+=("ingress-https:ingress-nginx:svc/ingress-nginx-controller:8443:443")
fi

PIDS=()
cleanup() {
  trap - INT TERM EXIT
  echo
  step "Stopping tunnels"
  for p in "${PIDS[@]}"; do pkill -P "$p" 2>/dev/null || true; kill "$p" 2>/dev/null || true; done
  wait 2>/dev/null || true
  ok "All tunnels closed."
}
trap cleanup INT TERM EXIT

port_busy() { (exec 3<>"/dev/tcp/127.0.0.1/$1") 2>/dev/null && { exec 3>&-; return 0; } || return 1; }

step "Opening tunnels (Ctrl+C stops all)"
for entry in "${TUNNELS[@]}"; do
  IFS=: read -r name ns target lport rport <<<"$entry"
  if port_busy "$lport"; then
    warn "$name: localhost:$lport already in use — SKIPPED (is docker-compose or another tunnel running?)"
    continue
  fi
  ( while true; do
      kubectl -n "$ns" port-forward --address 127.0.0.1 "$target" "$lport:$rport" >/dev/null 2>&1 || true
      sleep 2   # pod restarted / connection dropped -> reconnect
    done ) &
  PIDS+=($!)
  info "$name  ->  localhost:$lport  ($ns/$target:$rport)"
done

# Credentials so nobody goes spelunking through secrets by hand.
pg_pass="$(kc get secret postgres-credentials -o jsonpath='{.data.POSTGRES_PASSWORD}' 2>/dev/null | base64 -d || true)"
pg_db="$(kc get secret postgres-credentials -o jsonpath='{.data.POSTGRES_DB}' 2>/dev/null | base64 -d || true)"
pg_user="$(kc get secret postgres-credentials -o jsonpath='{.data.POSTGRES_USER}' 2>/dev/null | base64 -d || true)"
rb_user="$(kc get secret rabbitmq-credentials -o jsonpath='{.data.RABBITMQ_DEFAULT_USER}' 2>/dev/null | base64 -d || true)"
rb_pass="$(kc get secret rabbitmq-credentials -o jsonpath='{.data.RABBITMQ_DEFAULT_PASS}' 2>/dev/null | base64 -d || true)"
mn_user="$(kc get secret minio-credentials -o jsonpath='{.data.MINIO_ROOT_USER}' 2>/dev/null | base64 -d || true)"
mn_pass="$(kc get secret minio-credentials -o jsonpath='{.data.MINIO_ROOT_PASSWORD}' 2>/dev/null | base64 -d || true)"

cat <<EOF

${_C_BOLD}Windows-side access (same URLs work inside WSL):${_C_RESET}
  App (full stack)   https://$INGRESS_HOST/            (needs Windows hosts entries -> 127.0.0.1
                                                        + mkcert CA in the Windows store)
  Backend direct     http://localhost:18080/health/ready   /metrics   /api/...
                     NOTE: Swagger UI is Development-only — the cluster runs Production, so
                     /swagger 404s here by design; use the frontend or curl.
  Frontend direct    http://localhost:13000/
  RabbitMQ mgmt      http://localhost:25672/    user: ${rb_user:-<no secret>}  pass: ${rb_pass:-<no secret>}
  MinIO console      http://localhost:19001/    user: ${mn_user:-<no secret>}  pass: ${mn_pass:-<no secret>}
  DataGrip           host=localhost port=15432 db=${pg_db:-forum_net} user=${pg_user:-forum} pass=${pg_pass:-<no secret>}
                     (15432, NOT 5432 — 5432 is the docker-compose Postgres; wrong port = wrong DB!)
EOF
if [[ "${MONITORING_ABSENT:-false}" == "true" ]]; then
  info "Monitoring tunnels skipped — namespace '$MONITORING_NAMESPACE' not found (make mon-up)."
else
  cat <<EOF
  Grafana            http://localhost:13001/    user: admin  pass: admin
                     (or https://grafana.$INGRESS_HOST via the ingress tunnel + hosts entry)
  Prometheus         http://localhost:19090/    (/targets, /rules, /alerts)
EOF
fi
if [[ "$INGRESS_FALLBACK" == "true" ]]; then
  cat <<EOF

${_C_YLW}Ingress bound to 8443 (fallback):${_C_RESET} the kernel blocks unprivileged ports <1024, so the
real-hostname browser flow (https://$INGRESS_HOST without a port) will NOT work yet. One-time fix:
  echo 'net.ipv4.ip_unprivileged_port_start=80' | sudo tee /etc/sysctl.d/99-forum-tunnels.conf
  sudo sysctl -p /etc/sysctl.d/99-forum-tunnels.conf
then re-run this script. (Smoke tests still work: curl -k https://$INGRESS_HOST:8443/ --resolve $INGRESS_HOST:8443:127.0.0.1)
EOF
fi
echo
info "Tunnels stay open until Ctrl+C."
wait
