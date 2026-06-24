#!/usr/bin/env bash
# Run a k6 profile against the deployed API. Profiles: smoke | demo | stress.
set -euo pipefail
PROFILE="${1:-smoke}"
BASE_URL="${2:-http://forum.local}"
k6 run -e BASE_URL="$BASE_URL" -e PROFILE="$PROFILE" load/k6/smoke.js
