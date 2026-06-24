#!/usr/bin/env bash
# Build image into minikube and apply manifests (migration Job first).
set -euo pipefail
eval "$(minikube docker-env)"
docker build -t forum-dotnet-api:local .
kubectl apply -f k8s/namespace.yaml
kubectl apply -f k8s/postgres/ -f k8s/backend/configmap.yaml
kubectl apply -f k8s/backend/migration-job.yaml
kubectl wait --for=condition=complete job/db-migrate -n forum-dotnet --timeout=180s
kubectl apply -f k8s/backend/ -f k8s/ingress/ -f k8s/network-policies/
