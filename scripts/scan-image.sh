#!/usr/bin/env bash
# Vulnerability-scan the application images with Trivy (HIGH/CRITICAL, fixed-only).
#   scripts/scan-image.sh                    # both images at $IMAGE_TAG (build first: make images)
#   scripts/scan-image.sh <image:tag> [...]  # explicit image refs instead
# Exits non-zero when any HIGH/CRITICAL finding WITH an available fix exists (CI-friendly).
# Runs on demand — no CI wiring in Phase 10a (the workflow only builds/tests today; a
# security.yml step is a listed-not-built future add). Baseline report:
# docs/runbooks/image-scan-baseline.md.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env
require_cmd docker "install Docker Engine or enable Docker Desktop's WSL integration"
require_cmd trivy "install via the Aqua apt repo or the single static binary — https://trivy.dev/latest/getting-started/installation/"

images=("$@")
[[ ${#images[@]} -gt 0 ]] || images=("$IMAGE_NAME:$IMAGE_TAG" "$IMAGE_NAME_WEB:$IMAGE_TAG")

failed=0
for img in "${images[@]}"; do
  docker image inspect "$img" >/dev/null 2>&1 || die "Image '$img' not found locally — build it first (make images)."
  step "Scanning $img (HIGH/CRITICAL, --ignore-unfixed)"
  if trivy image --severity HIGH,CRITICAL --ignore-unfixed --exit-code 1 "$img"; then
    ok "$img: no HIGH/CRITICAL findings with an available fix"
  else
    warn "$img: HIGH/CRITICAL findings — see the table above"
    failed=1
  fi
done

(( failed == 0 )) || die "Vulnerability scan failed."
ok "All scanned images are clean (HIGH/CRITICAL, fixed-only)."
