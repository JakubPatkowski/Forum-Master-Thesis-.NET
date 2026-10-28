#!/usr/bin/env bash
# Deploys Architecture B (gomx, kustomize overlay deploy/k8s/bench) into the SAME minikube profile as A for
# the thesis A/B benchmark (AB-ALIGNMENT-INSTRUCTIONS-A.md A-08). B's own `task k8s:*` targets assume a
# rootless-podman minikube and are not used here; the image is built straight into minikube's docker daemon
# (exactly how A's deploy.sh does it) so B's `imagePullPolicy: IfNotPresent` finds it without a registry.
#
#   scripts/gomx-bench-deploy.sh [--reset-db | --fresh] [--skip-build]
#
# Flow: build gomx:bench (+ gomx:bench-<sha>) → apply overlay (replicas 1: the bench seed runs in-process at
# first boot and must not race across pods) → gomx-tls secret (mkcert cert with the gomx.local SAN) → wait for
# "benchmark seed complete" → scale to 2 (the common Day-0 profile) → ServiceMonitor into A's Prometheus.
# --reset-db drops and recreates the gomx database first (a benchmark reset = reseed; deleting the namespace
# would NOT wipe minikube's hostpath PVC data). --fresh deletes the WHOLE gomx namespace AND wipes its leaked
# hostpath PVs/dirs in the VM (same as forum-dotnet's teardown.sh) — needed once when the bench overlay switches
# B's Postgres from 18-alpine (uid 70) to the Debian postgres:18 image A uses (uid 999 cannot read the old PGDATA).
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env

RESET_DB=false
FRESH=false
BUILD=true
while [[ $# -gt 0 ]]; do
  case "$1" in
    --reset-db) RESET_DB=true; shift ;;
    --fresh) FRESH=true; shift ;;
    --skip-build) BUILD=false; shift ;;
    *) die "Unknown argument: $1 (usage: gomx-bench-deploy.sh [--reset-db | --fresh] [--skip-build])" ;;
  esac
done

NS="$GOMX_NAMESPACE"
# Hubert's overlay (B-05) wins when his branch carries it; otherwise the identical A-hosted copy renders with the
# load restrictor lifted (its base is gomx's kustomization outside the overlay root).
OVERLAY="$GOMX_DIR/deploy/k8s/bench"
if [[ ! -f "$OVERLAY/kustomization.yaml" ]]; then
  OVERLAY="$REPO_ROOT/k8s/gomx-bench"
  # kustomize refuses absolute/outside-root bases (and mangles the gomx base when referenced from another root),
  # so the base is pre-rendered into the overlay dir (gitignored) on every deploy.
  kubectl kustomize "$GOMX_DIR/deploy/k8s" > "$OVERLAY/gomx-base.rendered.yaml" || die "rendering gomx's base kustomization failed"
fi
RENDER=(kubectl kustomize "$OVERLAY")
REPLICAS_AFTER_SEED="${GOMX_REPLICAS:-2}"
bk() { kubectl -n "$NS" "$@"; }

step "Preflight"
require_cmd docker; require_cmd kubectl
[[ -d "$GOMX_DIR/.git" ]] || die "gomx checkout not found at $GOMX_DIR (set GOMX_DIR in .env)."
[[ -f "$OVERLAY/kustomization.yaml" ]] || die "bench overlay missing: $OVERLAY"
mk status >/dev/null 2>&1 || die "minikube profile $MINIKUBE_PROFILE is not running (make mk-up)."
[[ -f "$REPO_ROOT/k8s/ingress/tls/tls.crt" ]] || die "no mkcert cert yet — run scripts/mkcert-tls.sh first (it adds the $GOMX_INGRESS_HOST SAN)."
GOMX_SHA="$(git -C "$GOMX_DIR" rev-parse --short HEAD)"
[[ -n "$(git -C "$GOMX_DIR" status --porcelain 2>/dev/null)" ]] && GOMX_SHA="$GOMX_SHA-dirty"
ok "gomx @ $GOMX_SHA ($(git -C "$GOMX_DIR" branch --show-current)), overlay $OVERLAY"

if $BUILD; then
  step "Building gomx:bench inside minikube's docker daemon ($GOMX_SHA)"
  eval "$(mk docker-env)"
  docker build -f "$GOMX_DIR/ContainerFile.dev" -t gomx:bench -t "gomx:bench-$GOMX_SHA" --build-arg GIT_SHA="$GOMX_SHA" "$GOMX_DIR"
  eval "$(mk docker-env -u)"
  ok "image gomx:bench (= gomx:bench-$GOMX_SHA) available to the node"
fi

if $FRESH; then
  step "Fresh start: deleting namespace $NS + wiping its hostpath data"
  kubectl delete namespace "$NS" --ignore-not-found --wait=true --timeout=300s >/dev/null || die "namespace $NS did not terminate."
  kubectl get pv -o jsonpath='{range .items[*]}{.metadata.name}{" "}{.spec.hostPath.path}{"\n"}{end}' 2>/dev/null \
    | awk -v ns="/tmp/hostpath-provisioner/$NS/" '$2 ~ ns {print $1}' \
    | xargs -r kubectl delete pv >/dev/null 2>&1 || true
  mk ssh -- "sudo rm -rf /tmp/hostpath-provisioner/$NS" >/dev/null 2>&1 \
    || warn "could not wipe /tmp/hostpath-provisioner/$NS in the VM — old PGDATA may be remounted."
  ok "namespace + data gone (the overlay recreates both; the first pod seeds)"
  RESET_DB=false
fi

if $RESET_DB && bk get statefulset postgres >/dev/null 2>&1; then
  step "Resetting the gomx database (benchmark reseed)"
  bk scale deploy/gomx --replicas=0 >/dev/null 2>&1 || true
  bk rollout status deploy/gomx --timeout=120s >/dev/null 2>&1 || true
  bk exec postgres-0 -- psql -U gomx -d postgres -v ON_ERROR_STOP=1 \
    -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname='gomx' AND pid <> pg_backend_pid();" \
    -c "DROP DATABASE IF EXISTS gomx;" -c "CREATE DATABASE gomx OWNER gomx;" >/dev/null
  ok "database gomx recreated (empty → the next pod seeds it)"
fi

step "Applying the bench overlay (replicas 1 for the first boot) — $OVERLAY"
"${RENDER[@]}" | kubectl apply -f -
# The overlay pins replicas: 1; a previous run may have scaled to 2 — snap back so a reseed cannot race.
bk scale deploy/gomx --replicas=1 >/dev/null
bash "$REPO_ROOT/scripts/mkcert-tls.sh" >/dev/null   # creates/refreshes gomx-tls now that the namespace exists
bk rollout status statefulset/postgres --timeout=240s
# startupProbe in the overlay allows up to 15 min of in-process seeding before liveness kicks in.
bk rollout status deploy/gomx --timeout=900s

step "Waiting for the bench seed / existing data"
SEEDED=false
for _ in $(seq 1 180); do
  if bk logs deploy/gomx --tail=400 2>/dev/null | grep -q 'benchmark seed complete'; then SEEDED=true; break; fi
  # An already-seeded DB skips the seed entirely — accept it when /stats already shows the corpus.
  STATS="$(curl -sk --resolve "$GOMX_INGRESS_HOST:443:$(mk ip)" "https://$GOMX_INGRESS_HOST/stats" 2>/dev/null || true)"
  if grep -Eq '"posts":[1-9][0-9]*' <<<"$STATS"; then SEEDED=true; break; fi
  sleep 5
done
$SEEDED || die "no 'benchmark seed complete' log line and /stats does not show 1600 posts — inspect: kubectl -n $NS logs deploy/gomx"

bk scale deploy/gomx --replicas="$REPLICAS_AFTER_SEED" >/dev/null
bk rollout status deploy/gomx --timeout=300s
ok "gomx scaled to $REPLICAS_AFTER_SEED replicas"

step "ServiceMonitor into A's Prometheus"
if kubectl get crd servicemonitors.monitoring.coreos.com >/dev/null 2>&1; then
  kubectl apply -f "$GOMX_DIR/deploy/monitoring/servicemonitor.yaml" >/dev/null
  ok "ServiceMonitor gomx/gomx applied (A's Prometheus selects all namespaces)"
else
  warn "Prometheus Operator CRDs absent (make mon-up) — ServiceMonitor skipped."
fi

echo
STATS="$(curl -sk --resolve "$GOMX_INGRESS_HOST:443:$(mk ip)" "https://$GOMX_INGRESS_HOST/stats" 2>/dev/null || echo '<unreachable>')"
ok "gomx up: https://$GOMX_INGRESS_HOST (resolves to $(mk ip) via k6 hosts pin / curl --resolve) — /stats: $STATS"
info "smoke:  TARGET=b scripts/run-load-test.sh smoke"
info "bench:  TARGET=b scripts/bench-run.sh demo --repeats 3     (scale A to 0 first: kubectl -n $K8S_NAMESPACE scale deploy/backend --replicas=0)"
