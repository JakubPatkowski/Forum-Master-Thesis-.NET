#!/usr/bin/env bash
# Build the API image straight into minikube's docker and apply all manifests
# in the correct order (secret -> postgres -> migration Job -> backend -> ingress).
# Idempotent: safe to re-run after code changes.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env
require_cmd minikube; require_cmd kubectl; require_cmd docker
warn_if_windows_mount

mk status >/dev/null 2>&1 || die "minikube profile '$MINIKUBE_PROFILE' is not running. Run scripts/setup-minikube.sh first."

step "Building $IMAGE_NAME:$IMAGE_TAG into minikube's docker daemon"
eval "$(mk docker-env)"
docker build -t "$IMAGE_NAME:$IMAGE_TAG" "$REPO_ROOT"
ok "Image built (imagePullPolicy: Never — no registry push needed)."

step "Namespace"
kubectl apply -f "$REPO_ROOT/k8s/namespace.yaml"

step "Postgres credentials secret"
if [[ -f "$REPO_ROOT/k8s/postgres/secret.yaml" ]]; then
  kubectl apply -f "$REPO_ROOT/k8s/postgres/secret.yaml"
  info "Applied k8s/postgres/secret.yaml"
else
  # Generate the secret the manifests expect. The backend + migration Job read
  # CONNECTION_STRING; the Postgres StatefulSet reads POSTGRES_* — provide both.
  CONN="Host=postgres;Database=$POSTGRES_DB;Username=$POSTGRES_USER;Password=$POSTGRES_PASSWORD"
  kubectl -n "$K8S_NAMESPACE" create secret generic postgres-credentials \
    --from-literal=POSTGRES_DB="$POSTGRES_DB" \
    --from-literal=POSTGRES_USER="$POSTGRES_USER" \
    --from-literal=POSTGRES_PASSWORD="$POSTGRES_PASSWORD" \
    --from-literal=CONNECTION_STRING="$CONN" \
    --dry-run=client -o yaml | kubectl apply -f -
  info "Generated dev secret 'postgres-credentials' (POSTGRES_* + CONNECTION_STRING)."
fi

step "Postgres + backend config"
kubectl apply \
  -f "$REPO_ROOT/k8s/postgres/statefulset.yaml" \
  -f "$REPO_ROOT/k8s/postgres/service.yaml" \
  -f "$REPO_ROOT/k8s/backend/configmap.yaml"
kc rollout status statefulset/postgres --timeout=180s

step "Database migration Job"
kc delete job db-migrate --ignore-not-found   # Jobs are ~immutable; recreate for a clean run
kubectl apply -f "$REPO_ROOT/k8s/backend/migration-job.yaml"
if ! kc wait --for=condition=complete job/db-migrate --timeout=180s; then
  kc logs job/db-migrate --tail=50 || true
  die "Migration Job did not complete. Logs above."
fi
ok "Migrations applied."

step "Backend + ingress"
kubectl apply \
  -f "$REPO_ROOT/k8s/backend/deployment.yaml" \
  -f "$REPO_ROOT/k8s/backend/service.yaml" \
  -f "$REPO_ROOT/k8s/backend/hpa.yaml" \
  -f "$REPO_ROOT/k8s/backend/pdb.yaml" \
  -f "$REPO_ROOT/k8s/ingress/ingress.yaml"
kc rollout status deployment/backend --timeout=180s

if [[ "$APPLY_NETWORK_POLICIES" == "true" ]]; then
  warn "Applying NetworkPolicies. 'default-deny-ingress' has no companion allow-rules yet,"
  warn "so it will block ingress->backend and backend->postgres. Enable only when allow-rules exist."
  kubectl apply -f "$REPO_ROOT/k8s/network-policies/"
fi

IP="$(mk ip)"
cat <<EOF

${_C_GRN}Deployed.${_C_RESET}
  Via ingress:    http://$INGRESS_HOST/api        (needs '$IP  $INGRESS_HOST' in /etc/hosts)
  Or port-forward: kubectl -n $K8S_NAMESPACE port-forward svc/backend 8080:80  ->  http://localhost:8080
  Status:          kubectl -n $K8S_NAMESPACE get pods,svc,ingress,hpa
  Logs:            kubectl -n $K8S_NAMESPACE logs -l app=backend -f
EOF
