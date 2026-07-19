#!/usr/bin/env bash
# Tear down the deployment / cluster.
#   scripts/teardown.sh              delete the app namespace AND wipe its data (true clean slate)
#   scripts/teardown.sh --keep-data  delete the namespace but LEAVE the volume data behind
#   scripts/teardown.sh --stop       stop the whole minikube VM (state + data preserved)
#   scripts/teardown.sh --delete     delete the minikube profile (destroys the whole VM)
#
# WHY --keep-data is NOT the default (the gotcha this script exists to hide):
#   minikube's hostpath storage-provisioner backs every PVC with a DETERMINISTIC directory
#   /tmp/hostpath-provisioner/<namespace>/<pvc-name> and does NOT honor the 'Delete' reclaim
#   policy — deleting the namespace (or even just the PVC) leaks the PV and LEAVES the directory
#   on disk. Recreating the same namespace (deploy.sh) then remounts the stale Postgres/RabbitMQ/
#   MinIO data: old users/threads reappear, and the fresh rabbitmq-credentials Secret no longer
#   matches the broker's persisted user DB (ACCESS_REFUSED). So a real reset MUST wipe the dirs.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env
require_cmd kubectl; require_cmd minikube

# Delete the leaked PVs bound to this namespace's claims and remove their backing hostpath dirs in
# the VM. Safe to run after the namespace is gone (nothing is mounting them). No-op if the VM is down.
wipe_namespace_data() {
  step "Wiping leaked PVs + hostpath data for '$K8S_NAMESPACE'"
  kubectl get pv -o jsonpath='{range .items[*]}{.metadata.name}{" "}{.spec.hostPath.path}{"\n"}{end}' 2>/dev/null \
    | awk -v ns="/tmp/hostpath-provisioner/$K8S_NAMESPACE/" '$2 ~ ns {print $1}' \
    | xargs -r kubectl delete pv >/dev/null 2>&1 || true
  if mk ssh -- "sudo rm -rf /tmp/hostpath-provisioner/$K8S_NAMESPACE" >/dev/null 2>&1; then
    ok "Stale data wiped — next deploy starts from an empty database."
  else
    warn "Could not reach the VM to wipe hostpath — data may persist on next deploy."
  fi
}

case "${1:-}" in
  --delete)
    step "Deleting minikube profile '$MINIKUBE_PROFILE'"
    mk delete ;;
  --stop)
    step "Stopping minikube profile '$MINIKUBE_PROFILE'"
    mk stop ;;
  --keep-data)
    step "Deleting namespace '$K8S_NAMESPACE' (keeping volume data)"
    kubectl delete namespace "$K8S_NAMESPACE" --ignore-not-found
    warn "Volume data kept — the next deploy will remount the old Postgres/RabbitMQ/MinIO data." ;;
  ""|--wipe)
    step "Deleting namespace '$K8S_NAMESPACE'"
    kubectl delete namespace "$K8S_NAMESPACE" --ignore-not-found
    wipe_namespace_data ;;
  *)
    die "Unknown arg '$1' (use: --stop | --delete | --keep-data | no arg for a clean-slate namespace reset)" ;;
esac
ok "Done."
