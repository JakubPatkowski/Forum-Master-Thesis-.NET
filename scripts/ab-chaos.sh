#!/usr/bin/env bash
# Fault-injection (resilience) run for ONE architecture under the demo load (thesis harness h4, 2026-10-18):
# the k6 demo profile runs as usual while faults are injected at fixed offsets; every k6 metric point is streamed
# to a CSV so ab_chaos_report.py can draw the per-5-s error/latency timeline around each fault, count the failed
# requests it caused and measure the recovery time (3 clean buckets: no failures, p95 ≤ 2 × the pre-fault p95).
# The J8 receipt probes of the same run show whether realtime pushes were lost (A: durable outbox → replayed after
# the pod comes back; B: Redis pub/sub → a push during the fault is gone).
#
#   scripts/ab-chaos.sh --target a|b [--fault pod-crash|pod-delete|db-restart|both] [--at 150,330]
#                       [--profile demo] [--no-reseed] [--label x]
#   make ab-chaos ARGS="--target a --fault both"      # then the same for --target b
#
# Faults (identical mechanics on both sides — plain kubectl against the app's namespace):
#   pod-crash   one app pod killed with --grace-period=0 --force (a crash: no preStop, no drain)
#   pod-delete  one app pod deleted gracefully (a rolling restart: preStop + terminationGracePeriod)
#   db-restart  the postgres-0 pod deleted (the StatefulSet recreates it; every app connection breaks)
#   both        pod-crash at the first offset, db-restart at the second
# Offsets (--at) are seconds after the k6 process started; LOGIN_POOL is kept small (50) so setup takes well under
# a minute on A and the faults land inside the measured window on both targets. The actual fault timestamps are
# recorded in faults.json; the report aligns the timeline on them, so offset drift does not matter.
# Archive: thesis/results/chaos/<stamp>-<target>-<fault>[-label]/
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env
source "$LIB_DIR/lib-bench.sh"

TARGET=""
FAULT="both"
AT="150,330"
PROFILE="demo"
LABEL=""
RESEED=true
while [[ $# -gt 0 ]]; do
  case "$1" in
    --target) TARGET="${2:?--target needs a|b}"; shift 2 ;;
    --fault) FAULT="${2:?--fault needs pod-crash|pod-delete|db-restart|both}"; shift 2 ;;
    --at) AT="${2:?--at needs seconds[,seconds]}"; shift 2 ;;
    --profile) PROFILE="${2:?}"; shift 2 ;;
    --label) LABEL="-${2:?}"; shift 2 ;;
    --no-reseed) RESEED=false; shift ;;
    -h|--help) sed -n '2,22p' "$0"; exit 0 ;;
    *) die "Unknown argument: $1 (see --help)" ;;
  esac
done
[[ "$TARGET" == a || "$TARGET" == b ]] || die "--target a|b is required"
case "$FAULT" in pod-crash|pod-delete|db-restart|both) ;; *) die "unknown --fault '$FAULT'" ;; esac
IFS=',' read -r -a OFFSETS <<<"$AT"
if [[ "$FAULT" == both ]]; then
  (( ${#OFFSETS[@]} >= 2 )) || die "--fault both needs two offsets (--at t1,t2)"
  FAULTS=("pod-crash:${OFFSETS[0]}" "db-restart:${OFFSETS[1]}")
else
  FAULTS=()
  for off in "${OFFSETS[@]}"; do FAULTS+=("$FAULT:$off"); done
fi

step "Preflight"
require_cmd k6; require_cmd kubectl; require_cmd python3
mk status >/dev/null 2>&1 || die "minikube profile $MINIKUBE_PROFILE is not running (make mk-up)."
ab_kubectl "$TARGET" get deployment "$(ab_deploy "$TARGET")" >/dev/null 2>&1 || die "$(ab_dir "$TARGET") is not deployed."
ab_prom_start

STAMP="$(date +%Y%m%d-%H%M)"
RUN="$REPO_ROOT/thesis/results/chaos/$STAMP-$(ab_dir "$TARGET")-$FAULT$LABEL"
mkdir -p "$RUN"
exec > >(tee -a "$RUN/chaos.log") 2>&1
info "archive: $RUN"
cleanup() {
  local rc=$?
  set +e
  ab_restore_limiter_a
  ab_prom_stop
  exit "$rc"
}
trap cleanup EXIT

step "Common profile + fresh dataset"
other=$([[ "$TARGET" == a ]] && echo b || echo a)
ab_kubectl "$other" get deployment "$(ab_deploy "$other")" >/dev/null 2>&1 && ab_scale "$other" 0
ab_apply_profile "$TARGET"
ab_check_profile "$TARGET" >"$RUN/profile-check.txt" || warn "common-profile check failed (see profile-check.txt)"
if $RESEED; then
  ab_reseed "$TARGET"
  ok "dataset reset to the Benchmark seed, fresh pods"
fi
ab_meta "$TARGET" "$RUN/meta.json"
TARGET="$TARGET" RESULTS_DIR="$RUN/warmup" LOGIN_POOL=2 bash "$LIB_DIR/run-load-test.sh" warmup "$(ab_base "$TARGET")" >/dev/null 2>&1 \
  || warn "warm-up reported a failure (discarded)."

step "k6 $PROFILE with faults: ${FAULTS[*]} (seconds after k6 start)"
POINTS="$RUN/k6-points-$STAMP.csv.gz"
STARTED="$(date +%s)"
( TARGET="$TARGET" RESULTS_DIR="$RUN" LOGIN_POOL=50 K6_OUT="csv=$POINTS" bash "$LIB_DIR/run-load-test.sh" "$PROFILE" "$(ab_base "$TARGET")" ) &
K6_JOB=$!

echo "[]" >"$RUN/faults.json"
inject() {
  local fault="$1" ns label pod detail
  ns="$(ab_ns "$TARGET")"; label="$(ab_label "$TARGET")"
  case "$fault" in
    pod-crash)
      pod="$(kubectl -n "$ns" get pods -l "$label" --field-selector=status.phase=Running -o jsonpath='{.items[0].metadata.name}')"
      kubectl -n "$ns" delete pod "$pod" --grace-period=0 --force >/dev/null 2>&1
      detail="$pod killed (grace 0, force)" ;;
    pod-delete)
      pod="$(kubectl -n "$ns" get pods -l "$label" --field-selector=status.phase=Running -o jsonpath='{.items[0].metadata.name}')"
      kubectl -n "$ns" delete pod "$pod" >/dev/null 2>&1
      detail="$pod deleted gracefully" ;;
    db-restart)
      kubectl -n "$ns" delete pod postgres-0 >/dev/null 2>&1
      detail="postgres-0 deleted (StatefulSet recreates it)" ;;
  esac
  local at; at="$(date +%s)"
  python3 - "$RUN/faults.json" "$fault" "$at" "$detail" <<'PY'
import json, sys
path, fault, at, detail = sys.argv[1:5]
faults = json.load(open(path))
faults.append({"fault": fault, "at_epoch": int(at), "detail": detail})
json.dump(faults, open(path, "w"), indent=1)
PY
  warn "FAULT $fault at +$(( at - STARTED ))s: $detail"
}

for spec in "${FAULTS[@]}"; do
  fault="${spec%%:*}"; offset="${spec##*:}"
  while kill -0 "$K6_JOB" 2>/dev/null && (( $(date +%s) - STARTED < offset )); do sleep 2; done
  kill -0 "$K6_JOB" 2>/dev/null || { warn "k6 finished before the $fault offset ($offset s) — fault not injected"; continue; }
  inject "$fault"
done
wait "$K6_JOB" || warn "k6 reported threshold breaches (expected under faults — see the report)"
FINISHED="$(date +%s)"
ab_kubectl "$TARGET" get events --sort-by=.lastTimestamp >"$RUN/events.txt" 2>/dev/null || true
ab_kubectl "$TARGET" get pods -o wide >"$RUN/pods-after.txt" 2>/dev/null || true
if ls "$RUN"/journeys-*.json >/dev/null 2>&1; then
  ab_window_metrics "$TARGET" "$RUN" "$(( (FINISHED + 20) * 1000 ))" "$(( (FINISHED + 50) * 1000 ))" || warn "window metrics failed"
fi

step "Report"
python3 "$LIB_DIR/ab_chaos_report.py" "$RUN" && ok "report: $RUN/CHAOS-REPORT.md"
ab_scale "$TARGET" "$(ab_start_replicas)"
ok "chaos run complete: $RUN"
