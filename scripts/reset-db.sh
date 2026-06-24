#!/usr/bin/env bash
# DEV ONLY: drop + recreate the database volume, then re-run migrations.
set -euo pipefail
kubectl delete statefulset postgres -n forum-dotnet --ignore-not-found
kubectl delete pvc -l app=postgres -n forum-dotnet --ignore-not-found
kubectl apply -f k8s/postgres/
kubectl apply -f k8s/backend/migration-job.yaml
