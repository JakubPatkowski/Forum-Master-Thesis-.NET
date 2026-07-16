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
  # The Benchmark seed profile lives in its own database on the SAME Postgres server, so Development and
  # Benchmark datasets coexist without a container/volume wipe (Phase 9b, AMENDMENT A2).
  : "${POSTGRES_DB_BENCH:=forum_net_bench}"
  : "${POSTGRES_USER:=forum}"
  : "${POSTGRES_PASSWORD:=forum_dev_only}"
  : "${MINIO_ROOT_USER:=minio}"
  : "${MINIO_ROOT_PASSWORD:=minio_dev_only}"
  : "${MINIO_BUCKET:=forum}"
  # Cluster-only credentials (Phase 10b). Empty defaults mean deploy.sh GENERATES a random value
  # when it first creates the k8s secret (generate-if-missing — an existing in-cluster secret is
  # never overwritten). Set them in .env only if you want fixed values.
  : "${RABBITMQ_USER:=forum}"
  : "${RABBITMQ_PASSWORD:=}"
  : "${JWT_SIGNING_KEY:=}"
  : "${MINIKUBE_PROFILE:=forum}"
  # Phase 10b resource contract (§1 of PHASE-9-10-ENTERPRISE-PLAN.md): 6 vCPU / 10 GiB for the
  # minikube VM — requires `.wslconfig` memory=12GB (the VM shares WSL with k6, IDE, docker).
  # If WSL sees less RAM than this (preflight.sh checks), lower MINIKUBE_MEMORY in .env: the app
  # stack alone (no monitoring) runs fine in 8192.
  : "${MINIKUBE_CPUS:=6}"
  : "${MINIKUBE_MEMORY:=10240}"
  : "${MINIKUBE_DRIVER:=docker}"
  : "${K8S_NAMESPACE:=forum-dotnet}"
  : "${IMAGE_NAME:=forum-dotnet-api}"
  : "${IMAGE_NAME_WEB:=forum-dotnet-web}"
  # IMAGE_TAG defaults to git-<short-sha> of HEAD, plus "-dirty" when the working tree has
  # uncommitted changes — every image (and thus every benchmark number) maps to an exact,
  # inspectable build (Phase 10a). An explicit IMAGE_TAG (env or .env) always wins, so
  # `IMAGE_TAG=local` keeps the historical fixed-tag behavior for plain local iteration.
  if [[ -z "${IMAGE_TAG:-}" ]]; then
    local _sha
    if _sha="$(git -C "$REPO_ROOT" rev-parse --short HEAD 2>/dev/null)"; then
      IMAGE_TAG="git-${_sha}"
      [[ -n "$(git -C "$REPO_ROOT" status --porcelain 2>/dev/null)" ]] && IMAGE_TAG="${IMAGE_TAG}-dirty"
    else
      IMAGE_TAG="local"   # not a git checkout (e.g. source tarball) — historical fallback
    fi
  fi
  : "${INGRESS_HOST:=forum.local}"
  # Default flipped to true in Phase 10b: the 10-50 allow-rules now exist and setup-minikube.sh
  # provisions calico, so the policies are real and enforced. `false` remains an escape hatch.
  : "${APPLY_NETWORK_POLICIES:=true}"
  # Phase 10c monitoring stack — Helm chart versions, pinned on first install (2026-07-12,
  # `helm search repo <chart>`). NEVER install unpinned: reproducibility is a thesis requirement.
  # Tempo comes from grafana-community (the grafana/tempo chart is deprecated — repo migration;
  # updates land only in grafana-community/helm-charts after 2026-01-30).
  : "${MONITORING_NAMESPACE:=monitoring}"
  : "${KPS_VERSION:=87.15.1}"    # prometheus-community/kube-prometheus-stack (app v0.92.1)
  : "${LOKI_VERSION:=7.0.0}"     # grafana/loki (Loki 3.6.x, SingleBinary)
  : "${ALLOY_VERSION:=1.10.1}"   # grafana/alloy (Alloy v1.17.x)
  : "${TEMPO_VERSION:=2.2.3}"    # grafana-community/tempo (Tempo 2.10.x)
  : "${PGEXP_VERSION:=8.1.1}"    # prometheus-community/prometheus-postgres-exporter (v0.20.x)
  export POSTGRES_DB POSTGRES_USER POSTGRES_PASSWORD \
         MINIO_ROOT_USER MINIO_ROOT_PASSWORD MINIO_BUCKET \
         RABBITMQ_USER RABBITMQ_PASSWORD JWT_SIGNING_KEY \
         MINIKUBE_PROFILE MINIKUBE_CPUS MINIKUBE_MEMORY MINIKUBE_DRIVER \
         K8S_NAMESPACE IMAGE_NAME IMAGE_NAME_WEB IMAGE_TAG INGRESS_HOST APPLY_NETWORK_POLICIES \
         MONITORING_NAMESPACE KPS_VERSION LOKI_VERSION ALLOY_VERSION TEMPO_VERSION PGEXP_VERSION
}

# kubectl bound to our namespace
kc() { kubectl -n "$K8S_NAMESPACE" "$@"; }

# minikube bound to our profile
mk() { minikube -p "$MINIKUBE_PROFILE" "$@"; }

compose() { docker compose -f "$REPO_ROOT/compose.yaml" "$@"; }

# Ensure a database exists on the compose Postgres (idempotent). POSTGRES_USER is the instance superuser, so it
# may CREATE DATABASE; we connect to the always-present maintenance DB 'postgres' to do it. Used to spin up
# forum_net_bench beside forum_net without wiping the data volume (Phase 9b).
ensure_database() {
  local db="$1"
  compose exec -T postgres psql -U "$POSTGRES_USER" -d postgres -tAc \
    "SELECT 1 FROM pg_database WHERE datname = '$db'" | grep -q 1 && return 0
  step "Creating database '$db'"
  compose exec -T postgres psql -U "$POSTGRES_USER" -d postgres -c "CREATE DATABASE \"$db\"" >/dev/null \
    && ok "Database '$db' created" || die "Could not create database '$db'."
}
