#!/usr/bin/env bash
# Start (or reuse) a minikube cluster sized for local forum-dotnet runs.
# Config via .env: MINIKUBE_PROFILE, MINIKUBE_CPUS, MINIKUBE_MEMORY, MINIKUBE_DRIVER.
# Phase 10b: --cni=calico is REQUIRED — the default CNI (kindnet) ignores NetworkPolicy objects
# entirely, turning k8s/network-policies/ into decoration (G1). CNI cannot be swapped on a live
# profile: an existing non-calico cluster must be recreated (`make mk-down ARGS=--delete`).
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env
require_cmd minikube "https://minikube.sigs.k8s.io/docs/start/"
require_cmd kubectl  "https://kubernetes.io/docs/tasks/tools/"
warn_if_windows_mount

if mk status >/dev/null 2>&1; then
  ok "minikube profile '$MINIKUBE_PROFILE' already running."
  # Guard against a stale pre-10b cluster: NetworkPolicies silently no-op without calico.
  if ! kubectl get daemonset -n kube-system calico-node >/dev/null 2>&1; then
    warn "This cluster has NO calico — NetworkPolicies are NOT enforced (G1)."
    warn "Recreate it:  make mk-down ARGS=--delete && make mk-up   (CNI can't be swapped live)."
  fi
else
  step "Starting minikube (profile=$MINIKUBE_PROFILE cpus=$MINIKUBE_CPUS mem=${MINIKUBE_MEMORY}MB driver=$MINIKUBE_DRIVER cni=calico)"
  mk start \
    --cpus="$MINIKUBE_CPUS" \
    --memory="$MINIKUBE_MEMORY" \
    --driver="$MINIKUBE_DRIVER" \
    --cni=calico \
    --addons=ingress,metrics-server
fi

kubectl config use-context "$MINIKUBE_PROFILE" >/dev/null 2>&1 || true

# The ingress admission webhook rejects Ingress objects until the controller is up — wait here so
# deploy.sh never races it.
step "Waiting for ingress-nginx controller"
kubectl -n ingress-nginx rollout status deployment/ingress-nginx-controller --timeout=180s >/dev/null
ok "ingress-nginx ready"

# 10d #2 — response compression at INGRESS, not Kestrel: JSON feed pages compress ~5-10x and the
# CPU stays off the measured backend pods (B parity: B's SSR server also compresses). Controller-
# scoped, so it lives here with the addon, not in deploy.sh; merge-patch keeps any manually-set
# keys (e.g. hsts) intact, and the controller hot-reloads nginx on ConfigMap change — no restart.
step "Enabling gzip on ingress-nginx (10d)"
kubectl -n ingress-nginx patch configmap ingress-nginx-controller --type merge -p '{"data":{
  "use-gzip": "true",
  "gzip-types": "application/json application/problem+json application/javascript text/css text/plain text/html image/svg+xml",
  "gzip-min-length": "1024"
}}' >/dev/null
ok "ingress gzip on (application/json et al., min 1 KiB)"

IP="$(mk ip)"
ok "Cluster ready at $IP"

cat <<EOF

Access model (full walkthrough: docs/runbooks/wsl-minikube-setup.md):

  FROM WSL (quick curl iteration) — map the ingress host to the cluster IP once:
    echo "$IP  $INGRESS_HOST minio.$INGRESS_HOST" | sudo tee -a /etc/hosts
    curl -k https://$INGRESS_HOST/health/live        # (via port-forward for /health — not routed)

  FROM WINDOWS (browser / DataGrip) — the cluster IP is NOT reachable from Windows; use
  'make tunnels' (kubectl port-forward -> WSL localhost -> Windows localhost via WSL2's built-in
  localhost forwarding) and point C:\\Windows\\System32\\drivers\\etc\\hosts at 127.0.0.1 instead:
    127.0.0.1  $INGRESS_HOST minio.$INGRESS_HOST grafana.$INGRESS_HOST

Next steps:
  scripts/mkcert-tls.sh     # once: generate the forum-tls cert (mkcert)
  scripts/deploy.sh --seed  # build images + deploy everything + seed dev data
  scripts/dev-tunnels.sh    # admin/browser tunnels (or: make tunnels)
EOF
