#!/usr/bin/env bash
# DEV ONLY: drop the in-cluster Postgres volume and re-run migrations.
# Phase 10b: also removes seed Jobs (stale Completed Jobs would block re-seeding), and re-applies
# the migration Job with the SAME image the running backend uses — deploy.sh substitutes the
# git-SHA tag at apply time, so the raw manifest's ':local' placeholder may not exist in minikube.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env
require_cmd kubectl; require_cmd minikube

warn "This DELETES ALL DATA in the in-cluster Postgres ($K8S_NAMESPACE)."
kc delete job db-migrate db-seed db-seed-benchmark --ignore-not-found
kc delete statefulset postgres --ignore-not-found
kc delete pvc -l app=postgres --ignore-not-found
kc delete pvc data-postgres-0 --ignore-not-found
kc wait --for=delete pod/postgres-0 --timeout=60s 2>/dev/null || true

# minikube's hostpath provisioner leaks the PV + backing directory on PVC delete (reclaimPolicy
# 'Delete' is not honored), so a fresh PVC at the deterministic path .../data-postgres-0 would
# REMOUNT the old data — the delete above would be a no-op. Wipe the leaked PV + the dir for real.
kubectl get pv -o jsonpath='{range .items[*]}{.metadata.name}{" "}{.spec.hostPath.path}{"\n"}{end}' 2>/dev/null \
  | awk -v p="/tmp/hostpath-provisioner/$K8S_NAMESPACE/data-postgres-0" '$2 == p {print $1}' \
  | xargs -r kubectl delete pv >/dev/null 2>&1 || true
mk ssh -- "sudo rm -rf /tmp/hostpath-provisioner/$K8S_NAMESPACE/data-postgres-0" >/dev/null 2>&1 \
  && ok "Postgres hostpath wiped" || warn "Could not wipe hostpath — old data may survive."

kubectl apply \
  -f "$REPO_ROOT/k8s/postgres/statefulset.yaml" \
  -f "$REPO_ROOT/k8s/postgres/service.yaml"
kc rollout status statefulset/postgres --timeout=180s

# Pin the migration image to whatever the live backend runs (exact same build).
DEPLOYED_IMAGE="$(kc get deployment backend -o jsonpath='{.spec.template.spec.containers[0].image}' 2>/dev/null || true)"
if [[ -z "$DEPLOYED_IMAGE" ]]; then
  die "No backend deployment found — run scripts/deploy.sh instead (it migrates as part of the full order)."
fi
sed "s|image: $IMAGE_NAME:local|image: $DEPLOYED_IMAGE|" "$REPO_ROOT/k8s/backend/migration-job.yaml" | kubectl apply -f -
kc wait --for=condition=complete job/db-migrate --timeout=300s
ok "Database reset and re-migrated (image: $DEPLOYED_IMAGE). Re-seed: scripts/seed-test-data.sh --cluster"
