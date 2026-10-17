#!/usr/bin/env bash
# Lighthouse harness (A-2) — the A-side half of the paired Core Web Vitals measurement
# (Q2/H2, forum-spec §9.5). Mirrors B's setup (gomx/lighthouserc.json + scripts/lighthouse.sh):
# same two-page choice (feed + one thread), 3 runs per page, and the IDENTICAL report-only
# budget numbers in frontend/lighthouserc.json, so both sides' reports read against one bar.
#
# WHAT IT NEEDS RUNNING (unlike B's self-contained binary, A is a CSR SPA — auditing it
# without a live API measures skeleton screens, not the product):
#   1. local infra + a MIGRATED, Development-SEEDED API on $API_URL
#      (make infra-up && make api ARGS=--migrate && make seed;  then make api)
#   2. nothing on $LH_PORT — this script builds a PRODUCTION frontend bundle
#      (next build, NEXT_PUBLIC_API_URL baked in) and serves it with next start.
#
# The audited thread id is resolved live from the API (first thread of the first public
# category — deterministic under the Development seed, but never hard-coded).
#
# Chrome: uses $CHROME_PATH if set, else .bench/chrome/chrome-wrapper (see --bootstrap-chrome),
# else chrome-launcher autodetects a system Chrome.
#   scripts/lighthouse.sh --bootstrap-chrome   # one-time, NO SUDO: downloads a portable
#      chrome-headless-shell (@puppeteer/browsers) plus the two missing NSS debs
#      (apt-get download + dpkg -x — no root needed) into .bench/chrome/ and writes a
#      wrapper that sets LD_LIBRARY_PATH. Works on any sudo-less Linux/WSL box.
#
# Output: .bench/lighthouse/*.report.{html,json} + manifest, and
# .bench/lighthouse-metrics.txt (OpenMetrics, schema-identical to B's — lighthouse-metrics.mjs).
set -uo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

API_URL="${API_URL:-http://localhost:5099}"
LH_PORT="${LH_PORT:-3000}"
BASE="http://localhost:${LH_PORT}"
CHROME_DIR="$REPO_ROOT/.bench/chrome"

# --- one-time portable-chrome bootstrap (no sudo) -----------------------------
if [[ "${1:-}" == "--bootstrap-chrome" ]]; then
  require_cmd npx "install Node.js"
  require_cmd dpkg-deb "any Debian/Ubuntu base has this"
  step "downloading chrome-headless-shell (portable, @puppeteer/browsers)"
  mkdir -p "$CHROME_DIR"
  npx -y @puppeteer/browsers install chrome-headless-shell@stable --path "$CHROME_DIR/browser"
  BIN="$(find "$CHROME_DIR/browser" -name chrome-headless-shell -type f | head -1)"
  [[ -n "$BIN" ]] || die "chrome-headless-shell download failed"
  step "extracting NSS libs from debs (apt-get download — no root)"
  (cd "$CHROME_DIR" && apt-get download libnss3 libnspr4 >/dev/null 2>&1 \
     && for d in ./*.deb; do dpkg-deb -x "$d" extracted; done && rm -f ./*.deb) \
    || die "apt-get download failed (needs network + a Debian/Ubuntu userland)"
  LIBDIR="$(find "$CHROME_DIR/extracted" -name 'libnss3.so' -printf '%h\n' | head -1)"
  [[ -n "$LIBDIR" ]] || die "libnss3.so not found in extracted debs"
  cat >"$CHROME_DIR/chrome-wrapper" <<EOF
#!/usr/bin/env bash
export LD_LIBRARY_PATH="$LIBDIR\${LD_LIBRARY_PATH:+:\$LD_LIBRARY_PATH}"
exec "$BIN" "\$@"
EOF
  chmod +x "$CHROME_DIR/chrome-wrapper"
  "$CHROME_DIR/chrome-wrapper" --version || die "wrapper smoke test failed"
  ok "portable Chrome ready: $CHROME_DIR/chrome-wrapper"
  exit 0
fi

require_cmd npm "install Node.js"
require_cmd curl
require_cmd python3

# --- resolve a browser --------------------------------------------------------
if [[ -z "${CHROME_PATH:-}" && -x "$CHROME_DIR/chrome-wrapper" ]]; then
  export CHROME_PATH="$CHROME_DIR/chrome-wrapper"
fi
info "Chrome: ${CHROME_PATH:-<chrome-launcher autodetect>}"

# --- preconditions: live seeded API ------------------------------------------
step "checking API at $API_URL"
curl -sf "$API_URL/health/live" >/dev/null \
  || die "API not reachable — start it first: make infra-up && make api ARGS=--migrate && make seed && make api"

# Resolve the audited thread dynamically: first thread of the first public category.
THREAD_ID="$(python3 - "$API_URL" <<'PY'
import json, sys, urllib.request
api = sys.argv[1]
cats = json.load(urllib.request.urlopen(f"{api}/api/content/categories"))
cats = cats if isinstance(cats, list) else cats.get("items", [])
for c in cats:
    cid = c.get("id")
    feed = json.load(urllib.request.urlopen(f"{api}/api/content/threads?categoryId={cid}"))
    items = feed if isinstance(feed, list) else feed.get("items", [])
    if items:
        print(items[0]["id"]); break
PY
)"
[[ -n "$THREAD_ID" ]] || die "no thread found via $API_URL — is the Development seed loaded? (make seed)"
ok "audited thread: /t/$THREAD_ID"

# --- production frontend build + start ---------------------------------------
step "building production frontend (NEXT_PUBLIC_API_URL=$API_URL)"
(cd "$REPO_ROOT/frontend" && NEXT_PUBLIC_API_URL="$API_URL" npm run build) \
  || die "next build failed"

# Kill any stale listener on the port — an old server would skew scores.
if command -v fuser >/dev/null 2>&1; then fuser -k "${LH_PORT}/tcp" 2>/dev/null || true; fi

step "starting next start on :$LH_PORT"
(cd "$REPO_ROOT/frontend" && exec setsid npx next start -p "$LH_PORT") >"$REPO_ROOT/.bench/next-start.log" 2>&1 &
SRV=$!
trap 'kill -- "-$SRV" 2>/dev/null || kill "$SRV" 2>/dev/null || true' EXIT

ready=""
for _ in $(seq 1 60); do
  curl -sf "$BASE" >/dev/null 2>&1 && { ready=1; break; }
  sleep 0.5
done
[[ -n "$ready" ]] || { cat "$REPO_ROOT/.bench/next-start.log" >&2; die "next start never became ready"; }

# --- collect / assert / upload (report-only, like B) --------------------------
cd "$REPO_ROOT"
rm -rf .bench/lighthouse .lighthouseci
mkdir -p .bench/lighthouse

# WSL leaks the WINDOWS TEMP/TMP paths into the environment; chrome-launcher would then
# create literal 'C:\Users\...' directories in the CWD. Pin both to a real tmpdir.
export TEMP=/tmp TMP=/tmp

LHCI=(npx -y @lhci/cli --config="$REPO_ROOT/frontend/lighthouserc.json")
"${LHCI[@]}" collect --url="$BASE/" --url="$BASE/t/$THREAD_ID" \
  || warn "lhci collect exited non-zero (report-only; continuing)"
"${LHCI[@]}" assert || warn "lhci assert reported budget warnings (report-only; continuing)"
"${LHCI[@]}" upload || warn "lhci upload exited non-zero (report-only; continuing)"

node scripts/lighthouse-metrics.mjs .bench/lighthouse .bench/lighthouse-metrics.txt || true

echo
ok "Lighthouse reports: .bench/lighthouse/*.report.html · summary: .bench/lighthouse-metrics.txt"
