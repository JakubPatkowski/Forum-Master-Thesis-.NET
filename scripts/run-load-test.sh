#!/usr/bin/env bash
# Run one k6 profile against the deployed API (Phase 9c). Profiles: smoke | demo | stress.
#
#   scripts/run-load-test.sh [smoke|demo|stress] [BASE_URL]
#
# While k6 runs, a sampler captures HPA state + summed backend pod CPU/mem every 5 s into
# $RESULTS_DIR/samples-<stamp>.json; afterwards the k6 summary JSON (the ===K6_SUMMARY_JSON_BEGIN===
# block emitted by main.js handleSummary) is extracted into $RESULTS_DIR/summary-<stamp>.json.
# RESULTS_DIR defaults to load/results/; bench-run.sh points it into the thesis archive instead.
#
# demo/stress REQUIRE the rate limiter raised (all k6 traffic shares ONE client IP — the default
# per-IP window would 429 the whole run): bench-run.sh raises + restores it. Any 429 fails the run
# via the forum_rate_limited threshold, so a forgotten raise is loud, never silently "benchmarked".
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env

PROFILE="${1:-smoke}"
BASE_URL="${2:-https://forum.local}"
case "$PROFILE" in smoke|demo|stress) ;; *) die "Unknown profile '$PROFILE' (use smoke|demo|stress)" ;; esac
require_cmd k6 "install: https://grafana.com/docs/k6/latest/set-up/install-k6/ (portable binary works)"

STAMP="$(date +%Y%m%d-%H%M%S)"
RESULTS_DIR="${RESULTS_DIR:-$REPO_ROOT/load/results}"
mkdir -p "$RESULTS_DIR"
RAW_OUT="$RESULTS_DIR/k6-raw-$STAMP.out"
SAMPLES_FILE="$RESULTS_DIR/samples-$STAMP.json"
SUMMARY_FILE="$RESULTS_DIR/summary-$STAMP.json"

# --- cluster context (all optional: a local-compose run just skips the sampler) -----------------
CLUSTER_UP=false
INGRESS_IP=""
if kc get deployment backend >/dev/null 2>&1; then
  CLUSTER_UP=true
  # /etc/hosts resolves forum.local to 127.0.0.1 first on this box (Windows-tunnel entry); k6 pins
  # both ingress hosts to the minikube IP itself via its `hosts` option — no sudo needed.
  INGRESS_IP="$(mk ip 2>/dev/null || true)"

  if [[ "$PROFILE" != "smoke" ]]; then
    # Effective global permit limit: an explicit deployment env override (bench-run.sh) wins over the configmap.
    LIMIT="$(kc get deployment backend -o jsonpath='{.spec.template.spec.containers[0].env[?(@.name=="RateLimiting__Global__PermitLimit")].value}' 2>/dev/null || true)"
    [[ -n "$LIMIT" ]] || LIMIT="$(kc get configmap backend-config -o jsonpath='{.data.RateLimiting__Global__PermitLimit}' 2>/dev/null || echo 100)"
    if (( LIMIT < 100000 )); then
      warn "RateLimiting__Global__PermitLimit is $LIMIT — $PROFILE WILL hit 429s and fail."
      warn "Use 'make bench ARGS=$PROFILE' (raises + restores it), or raise it manually:"
      warn "  kubectl -n $K8S_NAMESPACE set env deployment/backend RateLimiting__Global__PermitLimit=1000000 RateLimiting__Auth__PermitLimit=1000000"
    fi
  fi
fi

# --- sampler: every 5 s while k6 runs — HPA current/desired/CPU% + summed backend pod usage -----
SAMPLES_TMP="$(mktemp)"
SAMPLER_PID=""
sampler() {
  while :; do
    local ts hpa_line top cpu_m mem_mi pods
    ts="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    hpa_line="$(kc get hpa backend -o jsonpath='{.status.currentReplicas} {.status.desiredReplicas} {.status.currentMetrics[0].resource.current.averageUtilization}' 2>/dev/null || echo '')"
    top="$(kc top pods -l app=backend --no-headers 2>/dev/null || true)"
    cpu_m="$(awk '{gsub(/m/,"",$2); s+=$2} END{print s+0}' <<<"$top")"
    mem_mi="$(awk '{gsub(/Mi/,"",$3); s+=$3} END{print s+0}' <<<"$top")"
    pods="$(grep -c . <<<"$top" || true)"
    printf '{"ts":"%s","hpa_current":%s,"hpa_desired":%s,"hpa_cpu_pct":%s,"pods_running":%s,"backend_cpu_millicores":%s,"backend_mem_mib":%s}\n' \
      "$ts" "${hpa_line%% *}" "$(cut -d' ' -f2 <<<"$hpa_line " )" "$(cut -d' ' -f3 <<<"$hpa_line  " )" \
      "${pods:-0}" "${cpu_m:-0}" "${mem_mi:-0}" \
      | sed 's/:,/:null,/g; s/:}/:null}/g' >>"$SAMPLES_TMP"
    sleep 5
  done
}
if $CLUSTER_UP; then
  sampler & SAMPLER_PID=$!
fi
stop_sampler() {
  if [[ -n "$SAMPLER_PID" ]]; then
    kill "$SAMPLER_PID" 2>/dev/null || true
    wait "$SAMPLER_PID" 2>/dev/null || true
    SAMPLER_PID=""
  fi
}
trap stop_sampler EXIT

# --- run k6 --------------------------------------------------------------------------------------
step "k6 $PROFILE → $BASE_URL${INGRESS_IP:+  (ingress $INGRESS_IP)}"
set +e
k6 run --quiet \
  -e PROFILE="$PROFILE" \
  -e BASE_URL="$BASE_URL" \
  ${INGRESS_IP:+-e INGRESS_IP="$INGRESS_IP"} \
  ${LOGIN_POOL:+-e LOGIN_POOL="$LOGIN_POOL"} \
  "$REPO_ROOT/load/k6/main.js" >"$RAW_OUT"
K6_RC=$?
set -e
stop_sampler

# --- persist artifacts ---------------------------------------------------------------------------
if $CLUSTER_UP && [[ -s "$SAMPLES_TMP" ]]; then
  python3 -c 'import json,sys; print(json.dumps([json.loads(l) for l in open(sys.argv[1]) if l.strip()], indent=1))' \
    "$SAMPLES_TMP" >"$SAMPLES_FILE" 2>/dev/null || cp "$SAMPLES_TMP" "$SAMPLES_FILE"
fi
rm -f "$SAMPLES_TMP"

awk '/===K6_SUMMARY_JSON_BEGIN===/{f=1;next} /===K6_SUMMARY_JSON_END===/{f=0} f' "$RAW_OUT" >"$SUMMARY_FILE"
sed '/===K6_SUMMARY_JSON_BEGIN===/,$d' "$RAW_OUT"    # human-readable part of handleSummary
rm -f "$RAW_OUT"

[[ -s "$SUMMARY_FILE" ]] || die "k6 produced no summary JSON (rc=$K6_RC) — the run crashed before handleSummary."
info "summary:  $SUMMARY_FILE"
[[ -f "$SAMPLES_FILE" ]] && info "samples:  $SAMPLES_FILE"

# k6 rc 99 = thresholds breached. stress thresholds are informational by design (the profile
# documents the knee — PHASE-9-10 plan §9c), so only smoke/demo propagate the failure.
if (( K6_RC == 99 )); then
  if [[ "$PROFILE" == "stress" ]]; then
    warn "stress thresholds breached — informational by design, numbers are in the summary."
    exit 0
  fi
  die "thresholds breached (see summary above)"
fi
exit "$K6_RC"
