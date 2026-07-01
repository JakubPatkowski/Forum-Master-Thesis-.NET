#!/usr/bin/env bash
# DEV ONLY: drop the in-cluster Postgres volume and re-run migrations.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env
require_cmd kubectl

warn "This DELETES ALL DATA in the in-cluster Postgres ($K8S_NAMESPACE)."
kc delete statefulset postgres --ignore-not-found
kc delete pvc -l app=postgres --ignore-not-found
kc delete pvc data-postgres-0 --ignore-not-found

kubectl apply \
  -f "$REPO_ROOT/k8s/postgres/statefulset.yaml" \
  -f "$REPO_ROOT/k8s/postgres/service.yaml"
kc rollout status statefulset/postgres --timeout=180s

kc delete job db-migrate --ignore-not-found
kubectl apply -f "$REPO_ROOT/k8s/backend/migration-job.yaml"
kc wait --for=condition=complete job/db-migrate --timeout=180s
ok "Database reset and re-migrated."
