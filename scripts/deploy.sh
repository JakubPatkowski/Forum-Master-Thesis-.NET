#!/usr/bin/env bash
# Build both app images straight into minikube's docker and apply ALL manifests in dependency
# order (Phase 10b):
#   images -> namespace(PSS) -> secrets(x4 + tls) -> rbac -> postgres+rabbitmq+minio -> bucket Job
#   -> migration Job -> [seed Job] -> backend -> frontend -> ingress -> network-policies
# Idempotent: safe to re-run after code changes.
#
#   scripts/deploy.sh                  # deploy/refresh everything
#   scripts/deploy.sh --seed           # + Development seed Job (fail-fasts on a non-empty DB)
#   scripts/deploy.sh --seed-benchmark # + Benchmark seed Job (--force reset, for measured runs)
#
# Image tags: manifests pin the placeholder ':local'; apply_with_tag() substitutes the real
# $IMAGE_TAG (git-<sha>[-dirty], lib.sh) at apply time. Chosen over the 10a-suggested
# `kubectl set image` two-step because it produces ONE rollout per deploy instead of two
# (apply would first revert the image to :local, then set-image would roll again), keeps Jobs on
# the same exact tag, and still lands the SHA in rollout history for `kubectl rollout undo`.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env
require_cmd minikube; require_cmd kubectl; require_cmd docker; require_cmd openssl
warn_if_windows_mount

SEED_JOB=""
case "${1:-}" in
  --seed)           SEED_JOB="db-seed";           SEED_MANIFEST="seed-job.yaml" ;;
  --seed-benchmark) SEED_JOB="db-seed-benchmark"; SEED_MANIFEST="seed-job-benchmark.yaml" ;;
  "") ;;
  *) die "Unknown argument: $1 (use --seed | --seed-benchmark)" ;;
esac

mk status >/dev/null 2>&1 || die "minikube profile '$MINIKUBE_PROFILE' is not running. Run scripts/setup-minikube.sh first."

# Substitute the real image tag into a manifest at apply time (see header for why not set-image).
apply_with_tag() {
  sed -e "s|image: $IMAGE_NAME:local|image: $IMAGE_NAME:$IMAGE_TAG|" \
      -e "s|image: $IMAGE_NAME_WEB:local|image: $IMAGE_NAME_WEB:$IMAGE_TAG|" "$1" | kubectl apply -f -
}

# Create a secret only if it doesn't exist yet — an in-cluster secret is never overwritten, so
# generated passwords stay stable for the cluster's lifetime. Precedence: existing in-cluster
# secret > gitignored k8s/<dir>/secret.yaml > generated from .env/random.
ensure_secret() { # ensure_secret <name> <dir> <create-fn>
  local name="$1" dir="$2" create_fn="$3"
  if kc get secret "$name" >/dev/null 2>&1; then
    info "secret '$name' exists — kept as-is"
  elif [[ -f "$REPO_ROOT/k8s/$dir/secret.yaml" ]]; then
    kubectl apply -f "$REPO_ROOT/k8s/$dir/secret.yaml"
    info "applied k8s/$dir/secret.yaml"
  else
    "$create_fn"
    info "generated secret '$name'"
  fi
}

create_postgres_secret() {
  # Maximum Pool Size=30 is LOAD-BEARING (G8): 3 replicas x 30 + exporter + transient Job + psql
  # sessions <= max_connections=100. Full math: k8s/postgres/statefulset.yaml header.
  local conn="Host=postgres;Database=$POSTGRES_DB;Username=$POSTGRES_USER;Password=$POSTGRES_PASSWORD;Maximum Pool Size=30"
  kc create secret generic postgres-credentials \
    --from-literal=POSTGRES_DB="$POSTGRES_DB" \
    --from-literal=POSTGRES_USER="$POSTGRES_USER" \
    --from-literal=POSTGRES_PASSWORD="$POSTGRES_PASSWORD" \
    --from-literal=CONNECTION_STRING="$conn"
}

create_rabbitmq_secret() {
  # G14: a real user via RABBITMQ_DEFAULT_* (guest is loopback-only). The RabbitMq__* duplicates
  # are the exact .NET config keys the backend binds — one secret, zero drift.
  local pass="${RABBITMQ_PASSWORD:-$(openssl rand -hex 16)}"
  kc create secret generic rabbitmq-credentials \
    --from-literal=RABBITMQ_DEFAULT_USER="$RABBITMQ_USER" \
    --from-literal=RABBITMQ_DEFAULT_PASS="$pass" \
    --from-literal=RabbitMq__Username="$RABBITMQ_USER" \
    --from-literal=RabbitMq__Password="$pass"
}

create_minio_secret() {
  kc create secret generic minio-credentials \
    --from-literal=MINIO_ROOT_USER="$MINIO_ROOT_USER" \
    --from-literal=MINIO_ROOT_PASSWORD="$MINIO_ROOT_PASSWORD" \
    --from-literal=Storage__AccessKey="$MINIO_ROOT_USER" \
    --from-literal=Storage__SecretKey="$MINIO_ROOT_PASSWORD"
}

create_backend_secret() {
  local key="${JWT_SIGNING_KEY:-$(openssl rand -base64 48)}"
  kc create secret generic backend-secrets --from-literal=Jwt__SigningKey="$key"
}

run_job() { # run_job <name> <manifest> [timeout] — Jobs are immutable: recreate for a clean run
  local name="$1" manifest="$2" timeout="${3:-300s}"
  kc delete job "$name" --ignore-not-found >/dev/null
  apply_with_tag "$REPO_ROOT/k8s/$manifest"
  if ! kc wait --for=condition=complete "job/$name" --timeout="$timeout"; then
    kc logs "job/$name" --tail=50 || true
    die "Job '$name' did not complete. Logs above."
  fi
}

step "Building images into minikube's docker daemon ($IMAGE_TAG)"
eval "$(mk docker-env)"
# build-images.sh builds backend + frontend; the frontend bakes NEXT_PUBLIC_API_URL at build time
# (default https://$INGRESS_HOST — must match the ingress TLS host, see build-images.sh).
bash "$LIB_DIR/build-images.sh"
ok "Images built (imagePullPolicy: Never — no registry push needed)."

step "Namespace (PSS restricted) + ServiceAccounts"
kubectl apply -f "$REPO_ROOT/k8s/namespace.yaml" -f "$REPO_ROOT/k8s/rbac.yaml"

step "Secrets (generate-if-missing)"
ensure_secret postgres-credentials postgres create_postgres_secret
ensure_secret rabbitmq-credentials rabbitmq create_rabbitmq_secret
ensure_secret minio-credentials    minio    create_minio_secret
ensure_secret backend-secrets      backend  create_backend_secret
# TLS cert is the one secret we never generate here — mkcert must mint it (trusted locally).
if ! kc get secret forum-tls >/dev/null 2>&1; then
  TLS_DIR="$REPO_ROOT/k8s/ingress/tls"
  if [[ -f "$TLS_DIR/tls.crt" && -f "$TLS_DIR/tls.key" ]]; then
    kc create secret tls forum-tls --cert="$TLS_DIR/tls.crt" --key="$TLS_DIR/tls.key"
    info "created secret 'forum-tls' from $TLS_DIR"
  else
    die "TLS secret 'forum-tls' missing and no cert files in k8s/ingress/tls/. Run scripts/mkcert-tls.sh first (one-time)."
  fi
fi

step "Infrastructure: postgres + rabbitmq + minio (+ backend config — the migration Job consumes it)"
kubectl apply \
  -f "$REPO_ROOT/k8s/backend/configmap.yaml" \
  -f "$REPO_ROOT/k8s/postgres/statefulset.yaml" \
  -f "$REPO_ROOT/k8s/postgres/service.yaml" \
  -f "$REPO_ROOT/k8s/rabbitmq/configmap.yaml" \
  -f "$REPO_ROOT/k8s/rabbitmq/statefulset.yaml" \
  -f "$REPO_ROOT/k8s/rabbitmq/service.yaml" \
  -f "$REPO_ROOT/k8s/minio/statefulset.yaml" \
  -f "$REPO_ROOT/k8s/minio/service.yaml"
kc rollout status statefulset/postgres --timeout=180s
kc rollout status statefulset/rabbitmq --timeout=240s
kc rollout status statefulset/minio    --timeout=180s
ok "Infra up."

step "MinIO bucket Job"
run_job minio-create-bucket minio/create-bucket-job.yaml 120s
ok "Bucket ensured."

step "Database migration Job"
run_job db-migrate backend/migration-job.yaml 300s
ok "Migrations applied."

if [[ -n "$SEED_JOB" ]]; then
  step "Seed Job ($SEED_JOB)"
  run_job "$SEED_JOB" "backend/$SEED_MANIFEST" 600s
  ok "Seed complete."
fi

step "Backend"
kubectl apply \
  -f "$REPO_ROOT/k8s/backend/service.yaml" \
  -f "$REPO_ROOT/k8s/backend/hpa.yaml" \
  -f "$REPO_ROOT/k8s/backend/pdb.yaml"
apply_with_tag "$REPO_ROOT/k8s/backend/deployment.yaml"
kc rollout status deployment/backend --timeout=300s

step "Frontend"
kubectl apply -f "$REPO_ROOT/k8s/frontend/service.yaml"
apply_with_tag "$REPO_ROOT/k8s/frontend/deployment.yaml"
kc rollout status deployment/frontend --timeout=180s

step "Ingress (app + minio presign host)"
kubectl apply -f "$REPO_ROOT/k8s/ingress/ingress.yaml" -f "$REPO_ROOT/k8s/minio/ingress.yaml"

if [[ "$APPLY_NETWORK_POLICIES" == "true" ]]; then
  step "NetworkPolicies (default-deny + allow-rules; enforced by calico)"
  kubectl apply -f "$REPO_ROOT/k8s/network-policies/"
else
  warn "APPLY_NETWORK_POLICIES=false — skipping netpols (escape hatch; the default is true)."
fi

IP="$(mk ip)"
cat <<EOF

${_C_GRN}Deployed.${_C_RESET}  Image tag: $IMAGE_TAG
  From WSL:      https://$INGRESS_HOST  (needs '$IP  $INGRESS_HOST minio.$INGRESS_HOST' in /etc/hosts;
                 curl flag --cacert "\$(mkcert -CAROOT)/rootCA.pem" or -k)
  From Windows:  make tunnels   + Windows hosts file 127.0.0.1 entries + mkcert CA in the Windows
                 store — full walkthrough: docs/runbooks/wsl-minikube-setup.md
  Status:        kubectl -n $K8S_NAMESPACE get pods,svc,ingress,hpa,networkpolicy
  Logs:          kubectl -n $K8S_NAMESPACE logs -l app=backend -f
  Rollback:      kubectl -n $K8S_NAMESPACE rollout undo deployment/backend
EOF
