#!/usr/bin/env bash
# Bootstrap a local minikube cluster sized for a 16 GB dev machine.
set -euo pipefail
minikube start --cpus=4 --memory=8192 --addons=ingress,metrics-server
echo "Point an image build at minikube's docker: eval \$(minikube docker-env)"
