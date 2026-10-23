#!/usr/bin/env bash
# Run one k6 profile against a deployed target (Phase 9c, target-aware since the A/B alignment —
# AB-ALIGNMENT-INSTRUCTIONS-A.md A-02). Profiles: smoke | warmup | demo | stress | capacity | soak | spike
# (load/k6/main.js; soak = long stability run, spike = burst + recovery — both harness h4).
# Targets: a (forum-dotnet) | b (gomx).
#
#   scripts/run-load-test.sh [smoke|warmup|demo|stress|capacity] [BASE_URL]   # TARGET=a default
#   TARGET=b scripts/run-load-test.sh demo                                      # BASE_URL defaults per target
#
# While k6 runs, a sampler captures HPA state (when one exists) + summed app-pod CPU/mem (kubectl top) + the k6
# process's own CPU ticks/RSS every 5 s into $RESULTS_DIR/samples-<target>-<stamp>.json (k6 shares this host with
# the minikube VM — a saturated load generator would inflate latencies, so its load is part of the record).
# Afterwards the k6 summary JSON (the ===K6_SUMMARY_JSON_BEGIN=== block emitted by main.js handleSummary) is
# extracted into summary-<target>-<stamp>.json, the shared A/B per-journey block (incl. the measured window) into
# journeys-<target>-<stamp>.json and the human-readable tables into report-<target>-<stamp>.txt. RESULTS_DIR
# defaults to load/results/; bench-run.sh / ab-round.sh point it into the thesis archive instead.
#
# demo/stress REQUIRE the rate limiters neutralized (all k6 traffic shares ONE client IP): A — bench-run.sh
# raises + restores RateLimiting__*; B — the bench overlay sets GOMX_AUTH_RATELIMIT=0. Any 429 fails the run
# via the forum_rate_limited threshold, so a forgotten raise is loud, never silently "benchmarked".
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env

PROFILE="${1:-smoke}"
TARGET="${TARGET:-a}"
case "$PROFILE" in smoke|warmup|demo|stress|capacity|soak|spike) ;; *) die "Unknown profile '$PROFILE' (use smoke|warmup|demo|stress|capacity|soak|spike)" ;; esac
case "$TARGET" in
  a) BENCH_NS="$K8S_NAMESPACE";  BENCH_DEPLOY=backend; BENCH_HPA=backend; BENCH_LABEL="app=backend"; DEFAULT_BASE="https://$INGRESS_HOST" ;;
  b) BENCH_NS="$GOMX_NAMESPACE"; BENCH_DEPLOY=gomx;    BENCH_HPA=gomx;    BENCH_LABEL="app=gomx";    DEFAULT_BASE="https://$GOMX_INGRESS_HOST" ;;
  *) die "TARGET must be a|b (got '$TARGET')" ;;
esac
BASE_URL="${2:-$DEFAULT_BASE}"
require_cmd k6 "install: https://grafana.com/docs/k6/latest/set-up/install-k6/ (portable binary works)"

# kubectl bound to the TARGET's namespace (kc is A-only by construction in lib.sh).
bk() { kubectl -n "$BENCH_NS" "$@"; }

STAMP="$(date +%Y%m%d-%H%M%S)"
RESULTS_DIR="${RESULTS_DIR:-$REPO_ROOT/load/results}"
mkdir -p "$RESULTS_DIR"
RAW_OUT="$RESULTS_DIR/k6-raw-$TARGET-$STAMP.out"
SAMPLES_FILE="$RESULTS_DIR/samples-$TARGET-$STAMP.json"
SUMMARY_FILE="$RESULTS_DIR/summary-$TARGET-$STAMP.json"
JOURNEYS_FILE="$RESULTS_DIR/journeys-$TARGET-$STAMP.json"
REPORT_FILE="$RESULTS_DIR/report-$TARGET-$STAMP.txt"
K6_PID_FILE="$(mktemp)"

# --- cluster context (all optional: a local run just skips the sampler) -------------------------
CLUSTER_UP=false
INGRESS_IP=""
if bk get deployment "$BENCH_DEPLOY" >/dev/null 2>&1; then
  CLUSTER_UP=true
  # /etc/hosts resolves the ingress hosts to 127.0.0.1 first on this box (Windows-tunnel entry); k6 pins
  # the target's hosts to the minikube IP itself via its `hosts` option — no sudo needed.
  INGRESS_IP="$(mk ip 2>/dev/null || true)"

  if [[ "$PROFILE" != "smoke" && "$PROFILE" != "warmup" ]]; then
    if [[ "$TARGET" == "a" ]]; then
      # Effective global permit limit: an explicit deployment env override (bench-run.sh) wins over the configmap.
      LIMIT="$(bk get deployment backend -o jsonpath='{.spec.template.spec.containers[0].env[?(@.name=="RateLimiting__Global__PermitLimit")].value}' 2>/dev/null || true)"
      [[ -n "$LIMIT" ]] || LIMIT="$(bk get configmap backend-config -o jsonpath='{.data.RateLimiting__Global__PermitLimit}' 2>/dev/null || echo 100)"
      if (( LIMIT < 100000 )); then
        warn "RateLimiting__Global__PermitLimit is $LIMIT — $PROFILE WILL hit 429s and fail."
        warn "Use 'make bench ARGS=$PROFILE' (raises + restores it), or raise it manually:"
        warn "  kubectl -n $BENCH_NS set env deployment/backend RateLimiting__Global__PermitLimit=1000000 RateLimiting__Auth__PermitLimit=1000000"
      fi
    else
      AUTH_LIMIT="$(bk get deployment gomx -o jsonpath='{.spec.template.spec.containers[0].env[?(@.name=="GOMX_AUTH_RATELIMIT")].value}' 2>/dev/null || true)"
      if [[ "$AUTH_LIMIT" != "0" ]]; then
        warn "GOMX_AUTH_RATELIMIT is '${AUTH_LIMIT:-<unset → 10/min/IP>}' — setup() logins WILL 429. The bench overlay sets it to 0:"
        warn "  kubectl -n $BENCH_NS set env deployment/gomx GOMX_AUTH_RATELIMIT=0"
      fi
    fi
  fi
fi

# --- sampler: every 5 s while k6 runs — HPA current/desired/CPU% (null without an HPA) + summed app-pod usage --
# Field names are kept stable across targets (schema compatibility with earlier A archives):
# backend_cpu_millicores / backend_mem_mib mean "the measured app's pods" for whichever target runs.
SAMPLES_TMP="$(mktemp)"
SAMPLER_PID=""
CLK_TCK="$(getconf CLK_TCK 2>/dev/null || echo 100)"
# k6's own CPU: cumulative utime+stime ticks from /proc (the analysis differentiates consecutive samples) + RSS.
k6_proc() {
  local pid stat
  pid="$(cat "$K6_PID_FILE" 2>/dev/null || true)"
  if [[ -n "$pid" && -r "/proc/$pid/stat" ]]; then
    stat="$(awk '{print $14+$15}' "/proc/$pid/stat" 2>/dev/null || echo '')"
    printf '%s %s' "${stat:-null}" "$(awk '/^VmRSS:/{printf "%d", $2/1024}' "/proc/$pid/status" 2>/dev/null || echo null)"
  else
    printf 'null null'
  fi
}
sampler() {
  while :; do
    local ts hpa_line top cpu_m mem_mi pods k6p
    ts="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
    hpa_line="$(bk get hpa "$BENCH_HPA" -o jsonpath='{.status.currentReplicas} {.status.desiredReplicas} {.status.currentMetrics[0].resource.current.averageUtilization}' 2>/dev/null || echo '')"
    top="$(bk top pods -l "$BENCH_LABEL" --no-headers 2>/dev/null || true)"
    cpu_m="$(awk '{gsub(/m/,"",$2); s+=$2} END{print s+0}' <<<"$top")"
    mem_mi="$(awk '{gsub(/Mi/,"",$3); s+=$3} END{print s+0}' <<<"$top")"
    pods="$(grep -c . <<<"$top" || true)"
    k6p="$(k6_proc)"
    printf '{"ts":"%s","epoch":%s,"target":"%s","hpa_current":%s,"hpa_desired":%s,"hpa_cpu_pct":%s,"pods_running":%s,"backend_cpu_millicores":%s,"backend_mem_mib":%s,"k6_cpu_ticks":%s,"k6_rss_mib":%s,"clk_tck":%s}\n' \
      "$ts" "$(date +%s)" "$TARGET" "${hpa_line%% *}" "$(cut -d' ' -f2 <<<"$hpa_line " )" "$(cut -d' ' -f3 <<<"$hpa_line  " )" \
      "${pods:-0}" "${cpu_m:-0}" "${mem_mi:-0}" "${k6p%% *}" "${k6p##* }" "$CLK_TCK" \
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
trap 'stop_sampler; rm -f "$K6_PID_FILE"' EXIT

# --- run k6 --------------------------------------------------------------------------------------
step "k6 $PROFILE [target $TARGET] → $BASE_URL${INGRESS_IP:+  (ingress $INGRESS_IP)}"
set +e
# Optional open-model knobs (capacity profile) pass straight through to k6 when set.
K6_EXTRA=()
# Harness h4 variants (A/B round flags): AUTH_READS (reads carry the session), the J8 receipt-probe cadence,
# soak/spike sizing. Recorded by main.js in the journeys block, so every archive says which variant it measured.
for var in CAP_STEPS CAP_RAMP_S CAP_HOLD_S CAP_MAX_VUS WS_HOLD_MS LOGIN_STAGGER_MS \
           AUTH_READS WS_PROBE_INTERVAL_MS WS_PROBE_TIMEOUT_MS SOAK_MINUTES SOAK_VUS SPIKE_VUS; do
  [[ -n "${!var:-}" ]] && K6_EXTRA+=(-e "$var=${!var}")
done
# K6_OUT (e.g. csv=…/points.csv.gz) streams every metric point out of k6 — ab-chaos.sh needs the per-second error
# timeline around an injected fault; measured rounds leave it unset (the summary blocks are enough and cheaper).
k6 run --quiet ${K6_OUT:+--out "$K6_OUT"} \
  -e TARGET="$TARGET" \
  -e PROFILE="$PROFILE" \
  -e BASE_URL="$BASE_URL" \
  ${INGRESS_IP:+-e INGRESS_IP="$INGRESS_IP"} \
  ${LOGIN_POOL:+-e LOGIN_POOL="$LOGIN_POOL"} \
  "${K6_EXTRA[@]}" \
  "$REPO_ROOT/load/k6/main.js" >"$RAW_OUT" &
K6_PID=$!
echo "$K6_PID" >"$K6_PID_FILE"
wait "$K6_PID"
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
# The shared A/B per-journey block (forum-spec §9.4) — journeys/sec + duration percentiles + mix_version.
awk '/===K6_JOURNEYS_JSON_BEGIN===/{f=1;next} /===K6_JOURNEYS_JSON_END===/{f=0} f' "$RAW_OUT" >"$JOURNEYS_FILE"
[[ -s "$JOURNEYS_FILE" ]] || rm -f "$JOURNEYS_FILE"
sed '/===K6_SUMMARY_JSON_BEGIN===/,$d' "$RAW_OUT" | tee "$REPORT_FILE"    # human-readable part of handleSummary
rm -f "$RAW_OUT"

[[ -s "$SUMMARY_FILE" ]] || die "k6 produced no summary JSON (rc=$K6_RC) — the run crashed before handleSummary."
info "summary:  $SUMMARY_FILE"
[[ -f "$JOURNEYS_FILE" ]] && info "journeys: $JOURNEYS_FILE"
info "report:   $REPORT_FILE"
[[ -f "$SAMPLES_FILE" ]] && info "samples:  $SAMPLES_FILE"

# k6 rc 99 = thresholds breached. stress/capacity thresholds are informational by design (they document the
# knee — PHASE-9-10 plan §9c), and the warm-up is discarded, so only smoke/demo propagate the failure. A 429 is
# NOT informational anywhere: it means a limiter was measured instead of the application.
if (( K6_RC == 99 )); then
  if grep -q '"rate_limited":[1-9]' "$JOURNEYS_FILE" 2>/dev/null; then
    die "429 responses during the run — a rate limiter is active; this run is INVALID."
  fi
  if [[ "$PROFILE" == "stress" || "$PROFILE" == "capacity" || "$PROFILE" == "warmup" || "$PROFILE" == "spike" ]]; then
    warn "$PROFILE thresholds breached — informational by design, numbers are in the summary."
    exit 0
  fi
  die "thresholds breached (see summary above)"
fi
exit "$K6_RC"
