#!/usr/bin/env bash
# Build BOTH application images (backend API + frontend web) with one command.
#   scripts/build-images.sh               # forum-dotnet-api + forum-dotnet-web at $IMAGE_TAG
#   scripts/build-images.sh --no-cache    # extra args are passed through to both builds
# Tag: git-<short-sha>[-dirty] by default (see lib.sh), IMAGE_TAG=... overrides.
# The frontend bakes NEXT_PUBLIC_API_URL at BUILD time — API ORIGIN ONLY, never a /api
# path (see frontend/Dockerfile). Default: https://$INGRESS_HOST (Phase 10b: the cluster ingress
# terminates TLS and ssl-redirects, so the baked origin MUST be https or every API call bounces
# through a redirect the browser's fetch() won't follow for POSTs). Override for a local smoke
# test with NEXT_PUBLIC_API_URL=http://localhost:8080.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env
require_cmd docker "install Docker Engine or enable Docker Desktop's WSL integration"
require_cmd git
warn_if_windows_mount

api_url="${NEXT_PUBLIC_API_URL:-https://$INGRESS_HOST}"
case "$api_url" in
  */api | */api/)
    die "NEXT_PUBLIC_API_URL must be the API ORIGIN without the /api path (got '$api_url'). Call sites already prefix /api/..., so a path here 404s every request as /api/api/..." ;;
esac

step "Building $IMAGE_NAME:$IMAGE_TAG (backend, repo-root context)"
docker build -t "$IMAGE_NAME:$IMAGE_TAG" "$@" "$REPO_ROOT"

step "Building $IMAGE_NAME_WEB:$IMAGE_TAG (frontend, repo-root context, NEXT_PUBLIC_API_URL=$api_url)"
docker build -t "$IMAGE_NAME_WEB:$IMAGE_TAG" -f "$REPO_ROOT/frontend/Dockerfile" \
  --build-arg NEXT_PUBLIC_API_URL="$api_url" "$@" "$REPO_ROOT"

step "Image digests (record these next to benchmark results — tag -> exact build)"
for img in "$IMAGE_NAME:$IMAGE_TAG" "$IMAGE_NAME_WEB:$IMAGE_TAG"; do
  info "$img  $(docker image inspect --format '{{.Id}}' "$img")"
done
ok "Both images built. Scan: scripts/scan-image.sh   Cluster deploy: scripts/deploy.sh"
