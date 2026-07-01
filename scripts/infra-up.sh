#!/usr/bin/env bash
# Start the local backing services (Postgres, RabbitMQ, MinIO) with docker compose.
# The API itself is run separately: scripts/dev-api.sh (or from your IDE).
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env
require_cmd docker "install Docker Engine or enable Docker Desktop WSL integration"
warn_if_windows_mount

step "Starting local infra (docker compose up -d)"
compose up -d

step "Waiting for Postgres to report healthy"
cid="$(compose ps -q postgres)"
[[ -n "$cid" ]] || die "Postgres container not found."
for i in $(seq 1 30); do
  status="$(docker inspect -f '{{.State.Health.Status}}' "$cid" 2>/dev/null || echo starting)"
  [[ "$status" == "healthy" ]] && { ok "Postgres healthy"; break; }
  sleep 2
  (( i == 30 )) && die "Postgres did not become healthy in time (check: docker compose logs postgres)."
done

# Optional: pre-create a MinIO bucket if MINIO_BUCKET is set in .env.
if [[ -n "${MINIO_BUCKET:-}" ]]; then
  step "Ensuring MinIO bucket '$MINIO_BUCKET'"
  docker run --rm --network host --entrypoint sh minio/mc:latest -c \
    "mc alias set local http://localhost:9000 ${MINIO_ROOT_USER:-minio} ${MINIO_ROOT_PASSWORD:-minio_dev_only} && mc mb -p local/$MINIO_BUCKET" \
    && ok "Bucket ready" || warn "Could not create bucket (create it from the console instead)."
fi

cat <<EOF

${_C_GRN}Local infra is up:${_C_RESET}
  Postgres   localhost:5432    db=$POSTGRES_DB user=$POSTGRES_USER
  RabbitMQ   localhost:5672    management UI  http://localhost:15672  (guest / guest)
  MinIO      localhost:9000    console        http://localhost:9001   (minio / minio_dev_only)

Next:  scripts/dev-api.sh --migrate     # run the API against these services
Stop:  scripts/infra-down.sh            # add --volumes to wipe all data
EOF
