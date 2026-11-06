#!/usr/bin/env bash
# One-time TLS bootstrap (Phase 10b, G13): mint a locally-trusted cert for the ingress hosts and
# store it as the k8s secret `forum-tls`.
#
#   scripts/mkcert-tls.sh          # generate (if missing) + create/refresh the k8s secret
#   scripts/mkcert-tls.sh --force  # re-generate the cert files even if they exist
#
# Cert files land in k8s/ingress/tls/ (gitignored). SANs cover forum.local, minio.forum.local
# (presigned uploads — SigV4 binds the Host header) and grafana.forum.local (Phase 10c, free now).
#
# TRUST MODEL — two stores, don't confuse them:
#   * WSL:     `mkcert -install` puts the CA in the Linux system store (curl/wget in WSL trust it).
#   * WINDOWS: the BROWSER runs on Windows and trusts the WINDOWS certificate store — installing
#     the CA in WSL does nothing for it. Import the SAME rootCA.pem there (instructions printed
#     below; full walkthrough in docs/runbooks/wsl-minikube-setup.md).
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env
require_cmd mkcert "single Go binary: https://github.com/FiloSottile/mkcert/releases -> ~/.local/bin/mkcert && chmod +x"

TLS_DIR="$REPO_ROOT/k8s/ingress/tls"
# $GOMX_INGRESS_HOST (gomx.local) = Architecture B's ingress host in the shared A/B benchmark cluster —
# one cert, one CA for both systems (AB-ALIGNMENT-INSTRUCTIONS-A.md A-04).
HOSTS=("$INGRESS_HOST" "minio.$INGRESS_HOST" "grafana.$INGRESS_HOST" "$GOMX_INGRESS_HOST")
mkdir -p "$TLS_DIR"

# Install the CA into the WSL trust stores. Needs sudo for the system store; harmless if repeated.
# A failure (e.g. no sudo in a non-interactive shell) is non-fatal: the cert still works, WSL curl
# just needs --cacert "$(mkcert -CAROOT)/rootCA.pem".
step "mkcert CA (WSL store)"
mkcert -install 2>/dev/null && ok "CA installed in WSL trust store" \
  || warn "mkcert -install failed (no sudo?) — WSL curl needs --cacert \"\$(mkcert -CAROOT)/rootCA.pem\""

if [[ ! -f "$TLS_DIR/tls.crt" || "${1:-}" == "--force" ]]; then
  step "Generating cert for: ${HOSTS[*]}"
  mkcert -cert-file "$TLS_DIR/tls.crt" -key-file "$TLS_DIR/tls.key" "${HOSTS[@]}"
  ok "Cert written to k8s/ingress/tls/ (gitignored)"
else
  ok "Cert already exists (k8s/ingress/tls/tls.crt) — use --force to re-generate."
fi

# Create/refresh the secret if a cluster is reachable; deploy.sh also creates it from these files,
# so running this before the first `make mk-up` is fine.
if kubectl version >/dev/null 2>&1 && mk status >/dev/null 2>&1; then
  step "k8s secret forum-tls"
  kubectl apply -f "$REPO_ROOT/k8s/namespace.yaml" >/dev/null
  kc create secret tls forum-tls --cert="$TLS_DIR/tls.crt" --key="$TLS_DIR/tls.key" \
    --dry-run=client -o yaml | kubectl apply -f - >/dev/null
  ok "Secret forum-tls created/updated in namespace $K8S_NAMESPACE"
  # B's namespace needs its own copy (Secrets are namespaced); only once that namespace exists
  # (kubectl apply -k gomx/deploy/k8s/bench creates it) — re-run this script after deploying B.
  if kubectl get namespace "$GOMX_NAMESPACE" >/dev/null 2>&1; then
    kubectl -n "$GOMX_NAMESPACE" create secret tls gomx-tls --cert="$TLS_DIR/tls.crt" --key="$TLS_DIR/tls.key" \
      --dry-run=client -o yaml | kubectl apply -f - >/dev/null
    ok "Secret gomx-tls created/updated in namespace $GOMX_NAMESPACE"
  else
    info "Namespace $GOMX_NAMESPACE absent — gomx-tls is created when scripts/gomx-bench-deploy.sh runs."
  fi
else
  info "No running cluster — deploy.sh will create the secret from k8s/ingress/tls/ later."
fi

CAROOT="$(mkcert -CAROOT)"
cat <<EOF

${_C_BOLD}Windows trust (one-time, required for the Windows browser):${_C_RESET}
  The CA lives at: $CAROOT/rootCA.pem
  Option A (import into the Windows user Root store; confirmation dialog will pop up):
    cp "$CAROOT/rootCA.pem" /mnt/c/Users/<you>/Downloads/mkcert-rootCA.pem
    # then in a WINDOWS terminal:
    certutil -user -addstore Root %USERPROFILE%\\Downloads\\mkcert-rootCA.pem
  Option B (if mkcert.exe is installed on Windows, e.g. 'choco install mkcert'):
    # in a WINDOWS terminal — point it at the SAME CAROOT so both sides share one CA:
    set CAROOT=\\\\wsl\$\\Ubuntu$CAROOT && mkcert -install
  Verify: browse https://$INGRESS_HOST after 'make tunnels' + the Windows hosts entries — the
  padlock must show 'mkcert' as issuer with no warning.
EOF
