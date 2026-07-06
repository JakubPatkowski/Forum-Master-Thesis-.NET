#!/usr/bin/env bash
# Run the frontend (Next.js) dev server against the local API.
#   scripts/dev-web.sh              # npm install (if needed) + npm run dev
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env
require_cmd npm "install Node.js 20+ (see frontend/.nvmrc)"
warn_if_windows_mount

WEB="$REPO_ROOT/frontend"

if [[ ! -d "$WEB/node_modules" ]]; then
  step "Installing frontend dependencies (npm install)"
  (cd "$WEB" && npm install)
fi

# Defaults to :8080 to match `scripts/dev-api.sh`'s local port. Export NEXT_PUBLIC_API_URL
# yourself before calling this script to point elsewhere — a shell-exported value wins over
# both this default and any frontend/.env.local (Next.js never overrides already-set env vars).
export NEXT_PUBLIC_API_URL="${NEXT_PUBLIC_API_URL:-http://localhost:8080}"

step "Starting frontend  ->  http://localhost:3000  (API: $NEXT_PUBLIC_API_URL)"
cd "$WEB"
exec npm run dev
