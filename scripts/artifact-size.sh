#!/usr/bin/env bash
# Artifact-size measurement (A-8) — H1 size metrics, schema-identical to B's
# gomx/scripts/ci-artifact-metrics.sh + image-size-metrics.sh (OpenMetrics text: metric
# names artifact_size_bytes / web_assets_total_bytes / container_image_size_bytes), so the
# A and B outputs join into one thesis table.
#
# Measures:
#   - first-load web assets for the SPA route "/" — read from .next/app-build-manifest.json
#     (the exact JS+CSS files the browser downloads on first paint), raw + gzip. Needs a
#     production build first: (cd frontend && npm run build).
#   - the two container images (API + frontend) at $IMAGE_NAME[:_WEB]:$IMAGE_TAG — build
#     first with `make images`; missing images are skipped with a warning (B does the same).
#   - the Tauri desktop installer, IF present: pass TAURI_INSTALLER=<path> or let the
#     default glob find frontend/src-tauri/target/**/bundle output. The installer is built
#     on Windows (`npm run tauri:build` there — see frontend/src-tauri/README.md); on this
#     Linux box the step just skips with a note. Run this script on the Windows side too to
#     fill that row.
#
# Usage: scripts/artifact-size.sh [output-file]     (default .bench/artifact-metrics.txt)
set -uo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env

OUT="${1:-$REPO_ROOT/.bench/artifact-metrics.txt}"
mkdir -p "$(dirname "$OUT")"

raw() { wc -c <"$1" | tr -d ' '; }
gz() { gzip -9 -c "$1" | wc -c | tr -d ' '; }

MANIFEST="$REPO_ROOT/frontend/.next/app-build-manifest.json"
[[ -f "$MANIFEST" ]] || die "no production build — run: (cd frontend && npm run build)"

step "first-load assets for route / (from app-build-manifest.json)"
# Emits: <file>\t<js|css> for every asset of the "/" page entry (shell + page chunks).
mapfile -t ASSETS < <(python3 - "$MANIFEST" <<'PY'
import json, sys
m = json.load(open(sys.argv[1]))
for f in m["pages"].get("/page", []):
    kind = "css" if f.endswith(".css") else "js"
    print(f"{f}\t{kind}")
PY
)
[[ ${#ASSETS[@]} -gt 0 ]] || die "app-build-manifest has no '/page' entry — build broken?"

js_raw=0 js_gz=0 css_raw=0 css_gz=0
for entry in "${ASSETS[@]}"; do
  f="${entry%%$'\t'*}" kind="${entry##*$'\t'}"
  p="$REPO_ROOT/frontend/.next/$f"
  [[ -f "$p" ]] || { warn "asset missing on disk: $f"; continue; }
  r=$(raw "$p") g=$(gz "$p")
  if [[ "$kind" == "css" ]]; then
    css_raw=$((css_raw + r)); css_gz=$((css_gz + g))
  else
    js_raw=$((js_raw + r)); js_gz=$((js_gz + g))
  fi
done

{
  echo '# HELP artifact_size_bytes Size of a build artifact in bytes (raw; plus gzip for web assets).'
  echo '# TYPE artifact_size_bytes gauge'
  printf 'artifact_size_bytes{artifact="first_load_js",encoding="raw"} %s\n' "$js_raw"
  printf 'artifact_size_bytes{artifact="first_load_js",encoding="gzip"} %s\n' "$js_gz"
  printf 'artifact_size_bytes{artifact="first_load_css",encoding="raw"} %s\n' "$css_raw"
  printf 'artifact_size_bytes{artifact="first_load_css",encoding="gzip"} %s\n' "$css_gz"
  echo '# HELP web_assets_total_bytes Total gzip size of the CSS+JS the browser downloads.'
  echo '# TYPE web_assets_total_bytes gauge'
  printf 'web_assets_total_bytes{encoding="gzip"} %s\n' "$((js_gz + css_gz))"
} >"$OUT"

step "container images ($IMAGE_TAG)"
{
  echo '# HELP container_image_size_bytes Size of the built container image in bytes.'
  echo '# TYPE container_image_size_bytes gauge'
} >>"$OUT"
for pair in "api=$IMAGE_NAME:$IMAGE_TAG" "web=$IMAGE_NAME_WEB:$IMAGE_TAG"; do
  name="${pair%%=*}" ref="${pair#*=}"
  size="$(docker image inspect -f '{{.Size}}' "$ref" 2>/dev/null || true)"
  if [[ -z "$size" ]]; then
    warn "image '$ref' not found — build with 'make images' to fill this row"
    continue
  fi
  printf 'container_image_size_bytes{image="%s"} %s\n' "$name" "$size" >>"$OUT"
done

step "desktop installer (Tauri)"
INSTALLER="${TAURI_INSTALLER:-}"
if [[ -z "$INSTALLER" ]]; then
  INSTALLER="$(find "$REPO_ROOT/frontend/src-tauri/target" \
    \( -name '*.msi' -o -name '*-setup.exe' -o -name '*.AppImage' -o -name '*.deb' \) \
    -type f 2>/dev/null | head -1 || true)"
fi
if [[ -n "$INSTALLER" && -f "$INSTALLER" ]]; then
  printf 'artifact_size_bytes{artifact="desktop_installer",encoding="raw"} %s\n' "$(raw "$INSTALLER")" >>"$OUT"
  ok "installer: $INSTALLER"
else
  warn "no desktop installer found — it is built on Windows (tauri:build); run this script there or pass TAURI_INSTALLER=<path>"
fi

echo
ok "wrote $OUT:"
cat "$OUT"
