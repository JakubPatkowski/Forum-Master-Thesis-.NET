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
#
# CLUSTER MODE (harness h4, 2026-10-18 — the paired client-side measurement on the BENCHMARK deployment):
#   scripts/lighthouse.sh --cluster --target a [--preset mobile|desktop]     # https://forum.local via ingress
#   scripts/lighthouse.sh --cluster --target b [--preset mobile|desktop]     # https://gomx.local  via ingress
# Audits the SAME two pages (largest public space's feed + its first thread) of either architecture as deployed
# for the A/B round (Benchmark seed, bench resource profile, ingress TLS + gzip) with ONE Lighthouse version and
# identical flags — the fairest client-side comparison available here. `mobile` = Lighthouse's default form
# factor (Moto G Power class screen, simulated slow-4G + 4× CPU throttling — the "Android" view Hubert asked
# for); `desktop` = the built-in desktop preset. Chrome resolves the ingress hosts to the minikube IP through
# --host-resolver-rules (no /etc/hosts edit, no sudo) and accepts the mkcert certificate via
# --ignore-certificate-errors.
# Output: .bench/lighthouse-cluster-<target>-<preset>/ + .bench/lighthouse-metrics-cluster-<target>-<preset>.txt
set -uo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

API_URL="${API_URL:-http://localhost:5099}"
LH_PORT="${LH_PORT:-3000}"
BASE="http://localhost:${LH_PORT}"
CHROME_DIR="$REPO_ROOT/.bench/chrome"

CLUSTER=false
TARGET="a"
PRESET="mobile"
ARGS=()
while [[ $# -gt 0 ]]; do
  case "$1" in
    --cluster) CLUSTER=true; shift ;;
    --target) TARGET="${2:?--target needs a|b}"; shift 2 ;;
    --preset) PRESET="${2:?--preset needs mobile|desktop}"; shift 2 ;;
    *) ARGS+=("$1"); shift ;;
  esac
done
set -- "${ARGS[@]+"${ARGS[@]}"}"
case "$PRESET" in mobile|desktop) ;; *) die "unknown --preset '$PRESET' (mobile|desktop)" ;; esac

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

if $CLUSTER; then
  load_env
  require_cmd kubectl
  mk status >/dev/null 2>&1 || die "minikube profile $MINIKUBE_PROFILE is not running (make mk-up)."
  IP="$(mk ip)"
  case "$TARGET" in
    a) HOST="$INGRESS_HOST" ;;
    b) HOST="${GOMX_INGRESS_HOST:-gomx.local}" ;;
    *) die "--target must be a|b" ;;
  esac
  BASE="https://$HOST"
  CURL=(curl -sk --resolve "$HOST:443:$IP")
  step "cluster mode: $BASE → $IP, target $TARGET, preset $PRESET"
  "${CURL[@]}" -o /dev/null -w '%{http_code}\n' "$BASE/" | grep -q '^200$' || die "$BASE/ is not answering 200 through the ingress"

  # The audited pages: the LARGEST public space's feed page + the first thread on it (same rule on both targets).
  if [[ "$TARGET" == a ]]; then
    read -r SPACE_PATH THREAD_PATH < <(python3 - "$BASE" "$IP" "$HOST" <<'PYEOF'
import json, ssl, sys, urllib.request
base, ip, host = sys.argv[1:4]
ctx = ssl.create_default_context(); ctx.check_hostname = False; ctx.verify_mode = ssl.CERT_NONE
def get(path):
    req = urllib.request.Request(base.replace(host, ip) + path, headers={"Host": host, "Accept": "application/json"})
    return json.load(urllib.request.urlopen(req, context=ctx, timeout=30))
cats = [c for c in get("/api/content/categories") if c.get("visibility") == "public"]
best = None
for c in cats:
    feed = get(f"/api/content/threads?categoryId={c['id']}&limit=50")
    n = len(feed["items"])
    if feed["items"] and (best is None or n > best[0]):
        best = (n, c["slug"], feed["items"][0]["id"])
print(f"/c/{best[1]} /t/{best[2]}")
PYEOF
)
  else
    SLUG="$("${CURL[@]}" "$BASE/communities" | grep -o 'href="/c/[a-z0-9-]*"' | grep -v '/c/new"' | head -1 | sed 's|href="/c/\(.*\)"|\1|')"
    [[ -n "$SLUG" ]] || die "no public community found on $BASE/communities"
    THREAD="$("${CURL[@]}" "$BASE/c/$SLUG" | grep -o 'href="/thread/[0-9]*"' | head -1 | grep -o '[0-9]*')"
    [[ -n "$THREAD" ]] || die "no thread found on $BASE/c/$SLUG"
    SPACE_PATH="/c/$SLUG"; THREAD_PATH="/thread/$THREAD"
  fi
  ok "pages: $SPACE_PATH and $THREAD_PATH"

  OUT_DIR="$REPO_ROOT/.bench/lighthouse-cluster-$TARGET-$PRESET"
  rm -rf "$OUT_DIR" "$REPO_ROOT/.lighthouseci"
  mkdir -p "$OUT_DIR"
  export TEMP=/tmp TMP=/tmp
  RC_FILE="$OUT_DIR/lighthouserc.json"
  python3 - "$REPO_ROOT/frontend/lighthouserc.json" "$RC_FILE" "$OUT_DIR" "$PRESET" "$IP" "$INGRESS_HOST" "${GOMX_INGRESS_HOST:-gomx.local}" <<'PYEOF'
import json, sys
src, dst, out_dir, preset, ip, host_a, host_b = sys.argv[1:8]
rc = json.load(open(src))
settings = rc["ci"]["collect"].setdefault("settings", {})
flags = settings.get("chromeFlags", "")
# Lighthouse's CLI splits chromeFlags on whitespace but honours double quotes (parseChromeFlags) — the resolver
# rules contain spaces, so the value MUST be quoted or Chrome receives three broken flags (DNS_FAILURE).
settings["chromeFlags"] = (flags + " --ignore-certificate-errors "
                           f'--host-resolver-rules="MAP {host_a} {ip},MAP minio.{host_a} {ip},MAP {host_b} {ip}"').strip()
if preset == "desktop":
    settings["preset"] = "desktop"
else:
    settings.pop("preset", None)   # Lighthouse default = mobile emulation + simulated slow-4G throttling
rc["ci"]["collect"].pop("url", None)
rc["ci"]["upload"]["outputDir"] = out_dir
json.dump(rc, open(dst, "w"), indent=2)
PYEOF
  # chrome-launcher under WSL ignores TEMP/TMP: it derives a Windows LOCALAPPDATA path from $PATH and creates a
  # literal 'C:\Users\…\lighthouse.NNN' directory in the CWD. Running lhci from the output dir keeps that junk
  # out of the repo; it is swept afterwards.
  LHCI=(npx -y @lhci/cli --config="$RC_FILE")
  ( cd "$OUT_DIR" && "${LHCI[@]}" collect --url="$BASE$SPACE_PATH" --url="$BASE$THREAD_PATH" ) \
    || warn "lhci collect exited non-zero (report-only; continuing)"
  ( cd "$OUT_DIR" && "${LHCI[@]}" assert ) || warn "lhci assert reported budget warnings (report-only; continuing)"
  ( cd "$OUT_DIR" && "${LHCI[@]}" upload ) || warn "lhci upload exited non-zero (report-only; continuing)"
  find "$OUT_DIR" -maxdepth 1 -name 'C:*' -type d -exec rm -rf {} + 2>/dev/null || true
  node scripts/lighthouse-metrics.mjs "$OUT_DIR" "$REPO_ROOT/.bench/lighthouse-metrics-cluster-$TARGET-$PRESET.txt" || true
  echo
  ok "Lighthouse ($TARGET, $PRESET, cluster): $OUT_DIR · summary: .bench/lighthouse-metrics-cluster-$TARGET-$PRESET.txt"
  exit 0
fi

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

# chrome-launcher under WSL ignores the TEMP/TMP pin above (it rewrites TEMP from $PATH) and creates a literal
# 'C:\Users\…\lighthouse.NNN' directory in the CWD — run lhci from .bench/lighthouse and sweep it afterwards.
LHCI=(npx -y @lhci/cli --config="$REPO_ROOT/frontend/lighthouserc.json")
( cd .bench/lighthouse && "${LHCI[@]}" collect --url="$BASE/" --url="$BASE/t/$THREAD_ID" ) \
  || warn "lhci collect exited non-zero (report-only; continuing)"
( cd .bench/lighthouse && "${LHCI[@]}" assert ) || warn "lhci assert reported budget warnings (report-only; continuing)"
( cd .bench/lighthouse && "${LHCI[@]}" upload ) || warn "lhci upload exited non-zero (report-only; continuing)"
find .bench/lighthouse -maxdepth 1 -name 'C:*' -type d -exec rm -rf {} + 2>/dev/null || true

node scripts/lighthouse-metrics.mjs .bench/lighthouse .bench/lighthouse-metrics.txt || true

echo
ok "Lighthouse reports: .bench/lighthouse/*.report.html · summary: .bench/lighthouse-metrics.txt"
