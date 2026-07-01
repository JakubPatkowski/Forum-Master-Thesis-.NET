#!/usr/bin/env bash
# Start (or reuse) a minikube cluster sized for local forum-dotnet runs.
# Config via .env: MINIKUBE_PROFILE, MINIKUBE_CPUS, MINIKUBE_MEMORY, MINIKUBE_DRIVER.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env
require_cmd minikube "https://minikube.sigs.k8s.io/docs/start/"
require_cmd kubectl  "https://kubernetes.io/docs/tasks/tools/"
warn_if_windows_mount

if mk status >/dev/null 2>&1; then
  ok "minikube profile '$MINIKUBE_PROFILE' already running."
else
  step "Starting minikube (profile=$MINIKUBE_PROFILE cpus=$MINIKUBE_CPUS mem=${MINIKUBE_MEMORY}MB driver=$MINIKUBE_DRIVER)"
  mk start \
    --cpus="$MINIKUBE_CPUS" \
    --memory="$MINIKUBE_MEMORY" \
    --driver="$MINIKUBE_DRIVER" \
    --addons=ingress,metrics-server
fi

kubectl config use-context "$MINIKUBE_PROFILE" >/dev/null 2>&1 || true
IP="$(mk ip)"
ok "Cluster ready at $IP"

cat <<EOF

One-time: map the ingress host so http://$INGRESS_HOST works from WSL:
  echo "$IP  $INGRESS_HOST" | sudo tee -a /etc/hosts

Then deploy the app:
  scripts/deploy.sh
EOF
