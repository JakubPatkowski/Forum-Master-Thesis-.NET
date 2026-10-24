#!/usr/bin/env bash
# Repo-level CVE/secret/misconfig scan with trivy — the A-side half of the paired A/B scan
# (J-5 / H3): CVE counts are only comparable when both repos are scanned by the SAME tool,
# same flags, same day. The flags below are byte-identical to B's CI job
# (gomx/.gitlab-ci.yml `security:trivy-fs`): filesystem scan over the repo root covering
# third-party dependency CVEs (Directory.Packages.props / package-lock.json), leaked
# secrets, and Dockerfile/k8s/compose misconfig; HIGH/CRITICAL only; --ignore-unfixed keeps
# the signal to vulns with an available fix.
#
#   scripts/security-scan.sh              # scan THIS repo  -> trivy-report.json
#   scripts/security-scan.sh ../gomx      # paired scan of the B checkout (read-only:
#                                         #   report lands HERE as trivy-report-gomx.json)
#
# Scan-day protocol: run both commands back-to-back with the same trivy binary and record
# `trivy --version` (it is embedded in the JSON report) in the results table. Install:
# single static binary from the GitHub release tarball into ~/.local/bin (no sudo needed).
# Exits non-zero when findings exist (CI-friendly); the JSON artifact is written either way.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
require_cmd trivy "single static binary: https://github.com/aquasecurity/trivy/releases -> ~/.local/bin"

TARGET="${1:-$REPO_ROOT}"
[[ -d "$TARGET" ]] || die "Target directory '$TARGET' not found."
TARGET="$(cd "$TARGET" && pwd)"

if [[ "$TARGET" == "$REPO_ROOT" ]]; then
  REPORT="$REPO_ROOT/trivy-report.json"
else
  REPORT="$REPO_ROOT/trivy-report-$(basename "$TARGET").json"
fi

step "trivy $(trivy --version 2>/dev/null | head -1 | awk '{print $NF}') — fs scan of $TARGET"
info "scanners: vuln,secret,misconfig · severity: HIGH,CRITICAL · --ignore-unfixed (B-parity flags)"

# Local build artifacts that do NOT exist in a fresh checkout (B's CI scans a clean clone).
# Skipping them reproduces the same scanned surface locally; lockfiles/manifests still cover
# the dependency CVEs. Paths are relative to the scan target and harmless when absent.
SKIP_DIRS="node_modules,frontend/node_modules,frontend/.next,backend/src/**/bin,backend/src/**/obj,backend/tests/**/bin,backend/tests/**/obj,frontend/src-tauri/target,.trivycache,k8s/ingress/tls"

# Pass 1: JSON artifact (never fails the run — the report must exist for the results table).
trivy fs --scanners vuln,secret,misconfig --severity HIGH,CRITICAL --ignore-unfixed \
  --skip-dirs "$SKIP_DIRS" \
  --format json --output "$REPORT" "$TARGET"
ok "report written: $REPORT"

# Pass 2: human-readable table; non-zero exit on findings (cache is warm, so this is fast).
if trivy fs --scanners vuln,secret,misconfig --severity HIGH,CRITICAL --ignore-unfixed \
  --skip-dirs "$SKIP_DIRS" \
  --exit-code 1 --format table "$TARGET"; then
  ok "no HIGH/CRITICAL findings with an available fix"
else
  warn "HIGH/CRITICAL findings present — see the table above and $REPORT"
  exit 1
fi
