#!/usr/bin/env bash
# Seed the deterministic dataset (Phase 9b). Two profiles in isolated databases; a native `dotnet run -- seed`
# locally, or the one-shot k8s Job in the cluster. See docs/architecture/PHASE-9-10-ENTERPRISE-PLAN.md §9b.
#
#   scripts/seed-test-data.sh                       # Development profile → forum_net        (local)
#   scripts/seed-test-data.sh --benchmark           # Benchmark   profile → forum_net_bench  (local, --force)
#   scripts/seed-test-data.sh --cluster             # Development k8s Job  (db-seed)
#   scripts/seed-test-data.sh --benchmark --cluster # Benchmark   k8s Job  (db-seed-benchmark)
#
# Accepts the profile as a flag (--benchmark) or a bare word (development|benchmark), in any order.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env

PROFILE=development
TARGET=local
ALLOW_TRUNCATE=false
for arg in "$@"; do
  case "$arg" in
    --benchmark|benchmark) PROFILE=benchmark ;;
    --development|development) PROFILE=development ;;
    --cluster|cluster) TARGET=cluster ;;
    --local|local) TARGET=local ;;
    --force) ALLOW_TRUNCATE=true ;;
    *) die "Unknown argument: $arg (use [development|--benchmark] [--cluster] [--force])" ;;
  esac
done

API="$REPO_ROOT/backend/src/Bootstrap/Forum.Api"

if [[ "$TARGET" == "cluster" ]]; then
  require_cmd kubectl "install kubectl"
  if [[ "$PROFILE" == "benchmark" ]]; then
    job=db-seed-benchmark; manifest="$REPO_ROOT/k8s/backend/seed-job-benchmark.yaml"
  else
    job=db-seed;           manifest="$REPO_ROOT/k8s/backend/seed-job.yaml"
  fi
  step "Applying $job ($PROFILE profile)"
  # Pin the Job to the image the live backend runs — deploy.sh substitutes the git-SHA tag at
  # apply time (Phase 10b), so the manifest's ':local' placeholder may not exist in minikube.
  DEPLOYED_IMAGE="$(kc get deployment backend -o jsonpath='{.spec.template.spec.containers[0].image}' 2>/dev/null || true)"
  [[ -n "$DEPLOYED_IMAGE" ]] || die "No backend deployment found — deploy first (scripts/deploy.sh)."
  kc delete job "$job" --ignore-not-found >/dev/null 2>&1 || true   # Jobs are immutable; replace on re-run
  sed "s|image: $IMAGE_NAME:local|image: $DEPLOYED_IMAGE|" "$manifest" | kc apply -f -
  kc wait --for=condition=complete "job/$job" --timeout=600s || die "Seed Job did not complete (kubectl logs job/$job)."
  ok "Seed Job complete"
  kc logs "job/$job" --tail=20 || true
  exit 0
fi

# --- Local (docker compose) --------------------------------------------------
require_cmd dotnet "install the .NET 10 SDK"
require_cmd docker "install Docker Engine or enable Docker Desktop WSL integration"
warn_if_windows_mount

step "Ensuring local Postgres is up"
compose up -d postgres >/dev/null

if [[ "$PROFILE" == "benchmark" ]]; then
  DB="$POSTGRES_DB_BENCH"
  SEED_ARGS=(seed --benchmark --force)   # --force: bench-local is a reproducible reset, always safe to re-run
else
  DB="$POSTGRES_DB"
  SEED_ARGS=(seed)                        # Development: no --force by default → aborts on a non-empty DB (idempotency guard)
  if [[ "$ALLOW_TRUNCATE" == "true" ]]; then
    SEED_ARGS+=(--force)                  # --force: reset Development DB if explicitly requested
  fi
fi

ensure_database "$DB"

export ConnectionStrings__Forum="Host=localhost;Port=5432;Database=$DB;Username=$POSTGRES_USER;Password=$POSTGRES_PASSWORD"
export ASPNETCORE_ENVIRONMENT=Development

step "Applying migrations + views to '$DB'"
dotnet run --project "$API" --no-launch-profile -- migrate

step "Seeding '$DB' ($PROFILE profile)"
dotnet run --project "$API" --no-launch-profile -- "${SEED_ARGS[@]}"

step "Row counts + size for '$DB'"
compose exec -T postgres psql -U "$POSTGRES_USER" -d "$DB" -c \
  "SELECT 'users' AS table, count(*) FROM forum_identity.users
   UNION ALL SELECT 'categories', count(*) FROM forum_content.categories
   UNION ALL SELECT 'threads',    count(*) FROM forum_content.threads
   UNION ALL SELECT 'comments',   count(*) FROM forum_content.comments
   UNION ALL SELECT 'reactions',  count(*) FROM forum_engagement.reactions
   ORDER BY 1;"
compose exec -T postgres psql -U "$POSTGRES_USER" -d "$DB" -tAc \
  "SELECT 'database size: ' || pg_size_pretty(pg_database_size('$DB'));"

ok "Seed complete → database '$DB'"
