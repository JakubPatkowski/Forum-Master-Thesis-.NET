#!/usr/bin/env bash
# Run Forum.Api locally (dotnet) wired to the docker-compose infra.
#   scripts/dev-api.sh              # just run the API
#   scripts/dev-api.sh --migrate    # apply migrations/views first, then run
#   scripts/dev-api.sh --migrate-only
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env
require_cmd dotnet "install the .NET 10 SDK"
warn_if_windows_mount

API="$REPO_ROOT/backend/src/Bootstrap/Forum.Api"
export ConnectionStrings__Forum="Host=localhost;Port=5432;Database=$POSTGRES_DB;Username=$POSTGRES_USER;Password=$POSTGRES_PASSWORD"
export RabbitMq__Host="localhost"
export Storage__Endpoint="localhost:9000"
export Storage__AccessKey="$MINIO_ROOT_USER"
export Storage__SecretKey="$MINIO_ROOT_PASSWORD"
export Storage__Bucket="$MINIO_BUCKET"
export ASPNETCORE_ENVIRONMENT="Development"
export ASPNETCORE_URLS="http://localhost:8080"

case "${1:-}" in
  --migrate)
    step "Applying migrations + views"
    dotnet run --project "$API" --no-launch-profile -- migrate ;;
  --migrate-only)
    step "Applying migrations + views (only)"
    exec dotnet run --project "$API" --no-launch-profile -- migrate ;;
esac

step "Starting Forum.Api  ->  http://localhost:8080  (health: /health/live)"
# --no-launch-profile: otherwise `dotnet run` silently applies launchSettings.json's
# applicationUrl (5099) and overrides ASPNETCORE_URLS above without saying so.
exec dotnet run --project "$API" --no-launch-profile
