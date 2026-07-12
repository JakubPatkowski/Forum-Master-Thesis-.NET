#!/usr/bin/env bash
# Check the machine has everything needed to build, run and deploy forum-dotnet.
# Safe to run any time; it only reads state.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env

step "Preflight: host & toolchain"

if grep -qiE "(microsoft|wsl)" /proc/version 2>/dev/null; then
  ok "Running under WSL2"
elif [[ "$(uname -s)" == "Linux" ]]; then
  ok "Running on native Linux"
else
  warn "Host is $(uname -s) — the k8s path expects Linux/WSL."
fi
warn_if_windows_mount

MISSING=0
check() { # check <bin> <hint>
  if command -v "$1" >/dev/null 2>&1; then ok "$1 present"; else warn "$1 MISSING — $2"; MISSING=1; fi
}
check docker   "Docker Engine, or enable Docker Desktop's WSL integration"
check kubectl  "https://kubernetes.io/docs/tasks/tools/"
check minikube "https://minikube.sigs.k8s.io/docs/start/"
check dotnet   "install the .NET 10 SDK"
check node     "install Node.js 20+ (see frontend/.nvmrc) — needed for the frontend"
check npm      "ships with Node.js — needed for the frontend"
check k6       "optional — only used by scripts/run-load-test.sh"
check trivy    "optional — only used by scripts/scan-image.sh (https://trivy.dev)"
check mkcert   "optional but needed once for cluster TLS — scripts/mkcert-tls.sh (https://github.com/FiloSottile/mkcert/releases)"
check helm     "optional — Phase 10c monitoring stack only (https://helm.sh)"

if command -v node >/dev/null 2>&1; then
  node_major="$(node -v | sed -E 's/^v([0-9]+).*/\1/')"
  (( node_major >= 20 )) || warn "Node $(node -v) found — frontend/.nvmrc pins 20+."
fi

# Docker daemon reachable?
if command -v docker >/dev/null 2>&1; then
  if docker info >/dev/null 2>&1; then
    ok "docker daemon reachable"
  else
    warn "docker is installed but the daemon is not reachable (start Docker Desktop, or 'sudo service docker start')"
    MISSING=1
  fi
fi

# .NET SDK version
if command -v dotnet >/dev/null 2>&1; then
  info "dotnet SDKs: $(dotnet --list-sdks 2>/dev/null | awk '{print $1}' | paste -sd', ' -)"
  dotnet --list-sdks 2>/dev/null | grep -q '^10\.' || warn "No .NET 10 SDK found — global.json pins SDK 10."
fi

# RAM sanity for minikube. The Phase 10b/10c target is MINIKUBE_MEMORY=10240, which needs
# .wslconfig memory=12GB (the WSL VM also hosts k6, the IDE server and docker itself).
if [[ -r /proc/meminfo ]]; then
  total_mb=$(( $(awk '/MemTotal/{print $2}' /proc/meminfo) / 1024 ))
  info "WSL sees ${total_mb} MB RAM; minikube is configured for ${MINIKUBE_MEMORY} MB."
  if (( total_mb < MINIKUBE_MEMORY + 1024 )); then
    warn "Not enough RAM headroom for minikube (${MINIKUBE_MEMORY} MB requested, ${total_mb} MB visible)."
    warn "On Windows set %USERPROFILE%\\.wslconfig:  [wsl2]  memory=12GB  swap=4GB  processors=6  — then 'wsl --shutdown'."
    warn "Or lower MINIKUBE_MEMORY in .env (8192 runs the app stack; the 10c monitoring stack wants 10240)."
  fi
fi

# Windows localhost forwarding — the entire Windows-browser/DataGrip access story (make tunnels)
# rides on it. Best-effort read of .wslconfig through the /mnt/c interop mount.
for wslcfg in /mnt/c/Users/*/.wslconfig; do
  [[ -r "$wslcfg" ]] || continue
  if grep -qiE '^\s*localhostForwarding\s*=\s*false' "$wslcfg"; then
    warn "$wslcfg sets localhostForwarding=false — Windows will NOT reach WSL tunnels (make tunnels breaks)."
    warn "Remove that line (the default is true), then 'wsl --shutdown'."
  else
    ok "WSL2 localhost forwarding enabled ($wslcfg)"
  fi
done

echo
if [[ "$MISSING" -eq 0 ]]; then ok "All required tools are present."; else warn "Resolve the items above before continuing."; fi
