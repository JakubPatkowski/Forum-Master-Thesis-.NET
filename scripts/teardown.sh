#!/usr/bin/env bash
# Tear down the deployment / cluster.
#   scripts/teardown.sh            delete the app namespace (cluster keeps running)
#   scripts/teardown.sh --stop     stop the whole minikube VM (state preserved)
#   scripts/teardown.sh --delete   delete the minikube profile (destroys everything)
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env
require_cmd kubectl; require_cmd minikube

case "${1:-}" in
  --delete)
    step "Deleting minikube profile '$MINIKUBE_PROFILE'"
    mk delete ;;
  --stop)
    step "Stopping minikube profile '$MINIKUBE_PROFILE'"
    mk stop ;;
  *)
    step "Deleting namespace '$K8S_NAMESPACE'"
    kubectl delete namespace "$K8S_NAMESPACE" --ignore-not-found ;;
esac
ok "Done."
