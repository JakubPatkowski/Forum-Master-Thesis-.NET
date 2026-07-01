#!/usr/bin/env bash
# Shared helpers for the forum-dotnet dev/ops scripts.
# Every script starts with:  source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
set -euo pipefail

# --- Paths -------------------------------------------------------------------
LIB_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$LIB_DIR/.." && pwd)"

# --- Pretty logging ----------------------------------------------------------
if [[ -t 1 ]]; then
  _C_RESET=$'\033[0m'; _C_RED=$'\033[31m'; _C_GRN=$'\033[32m'
  _C_YLW=$'\033[33m'; _C_BLU=$'\033[34m'; _C_BOLD=$'\033[1m'
else
  _C_RESET=; _C_RED=; _C_GRN=; _C_YLW=; _C_BLU=; _C_BOLD=
fi
step() { printf '%s\n' "${_C_BOLD}${_C_BLU}==>${_C_RESET} ${_C_BOLD}$*${_C_RESET}"; }
info() { printf '%s\n' "    $*"; }
ok()   { printf '%s\n' "${_C_GRN}  [ok]${_C_RESET} $*"; }
warn() { printf '%s\n' "${_C_YLW}  [warn]${_C_RESET} $*" >&2; }
die()  { printf '%s\n' "${_C_RED}  [fail] $*${_C_RESET}" >&2; exit 1; }

# --- Guards ------------------------------------------------------------------
require_cmd() {
  # require_cmd <bin> [install hint]
  command -v "$1" >/dev/null 2>&1 || die "'$1' not found on PATH. ${2:-}"
}

warn_if_windows_mount() {
  # WSL: running from /mnt/<drive> talks to NTFS over the slow 9P bridge.
  case "$REPO_ROOT" in
    /mnt/*)
      warn "Repo lives on a Windows mount ($REPO_ROOT) — dotnet/docker/git will be SLOW."
      warn "Clone it into the Linux filesystem (e.g. ~/projects) for near-native speed." ;;
  esac
}

# --- Config (.env at repo root + sensible defaults) --------------------------
load_env() {
  if [[ -f "$REPO_ROOT/.env" ]]; then
    set -a; source "$REPO_ROOT/.env"; set +a
  fi
  : "${POSTGRES_DB:=forum_net}"
  : "${POSTGRES_USER:=forum}"
  : "${POSTGRES_PASSWORD:=forum_dev_only}"
  : "${MINIKUBE_PROFILE:=forum}"
  : "${MINIKUBE_CPUS:=4}"
  : "${MINIKUBE_MEMORY:=8192}"
  : "${MINIKUBE_DRIVER:=docker}"
  : "${K8S_NAMESPACE:=forum-dotnet}"
  : "${IMAGE_NAME:=forum-dotnet-api}"
  : "${IMAGE_TAG:=local}"
  : "${INGRESS_HOST:=forum.local}"
  : "${APPLY_NETWORK_POLICIES:=false}"
  export POSTGRES_DB POSTGRES_USER POSTGRES_PASSWORD \
         MINIKUBE_PROFILE MINIKUBE_CPUS MINIKUBE_MEMORY MINIKUBE_DRIVER \
         K8S_NAMESPACE IMAGE_NAME IMAGE_TAG INGRESS_HOST APPLY_NETWORK_POLICIES
}

# kubectl bound to our namespace
kc() { kubectl -n "$K8S_NAMESPACE" "$@"; }

# minikube bound to our profile
mk() { minikube -p "$MINIKUBE_PROFILE" "$@"; }

compose() { docker compose -f "$REPO_ROOT/compose.yaml" "$@"; }
