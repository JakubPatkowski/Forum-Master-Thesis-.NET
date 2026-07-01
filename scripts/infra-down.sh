#!/usr/bin/env bash
# Stop the local backing services. Pass --volumes (or -v) to also delete the
# data volumes for a completely fresh database next time.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env
require_cmd docker

case "${1:-}" in
  -v|--volumes)
    warn "Removing containers AND volumes — all local Postgres/MinIO data will be lost."
    compose down -v ;;
  *)
    compose down
    info "Data volumes kept. Re-run with --volumes to wipe them." ;;
esac
ok "Local infra stopped."
