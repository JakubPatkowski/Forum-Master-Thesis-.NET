#!/usr/bin/env bash
# One complete, unattended A/B MEASUREMENT ROUND (thesis harness v1, 2026-10-17). Both architectures, every
# profile of the plan, N repeats each, interleaved ABBA, every run from a freshly reseeded dataset — all results,
# metadata and a generated comparison report archived under thesis/results/rounds/<stamp>-<label>/.
#
#   scripts/ab-round.sh                                     # default plan: demo:5,stress:1,capacity:1
#   scripts/ab-round.sh --plan demo:3 --label quick
#   scripts/ab-round.sh --plan smoke:1 --label dryrun        # ~15 min end-to-end check of the whole pipeline
#   make ab-round ARGS="--plan demo:5,capacity:1"
#
# Per measured run (target t, profile p, repeat k):
#   1. the OTHER architecture's app is scaled to 0 (runs never overlap; its DB/broker stay up and idle — they are
#      part of its namespace footprint and are reported as such)
#   2. t is reset to the pristine Benchmark seed (A: seed Job --force + rollout restart; B: DROP/CREATE DB + boot
#      seed) and comes up with AB_REPLICAS fresh pods — identical dataset AND fresh processes for every run on both
#      sides (the write mix adds ~4–5k rows per demo run; without the reset run k would measure a bigger forum)
#   3. the live objects are checked against the common profile (lib-bench.sh ab_check_profile) — a drifted
#      cluster aborts the round instead of producing an unfair number (--allow-mismatch records and continues)
#   4. a 60 s warm-up (discarded): every journey incl. ~6 % writes, so JIT/tiered compilation, pools and caches are
#      warm on every measured code path; it adds only ~30 comments + ~30 reactions (same on both sides, recorded)
#   5. the measured k6 run (run-load-test.sh) → summary / journeys (incl. the scenario window) / samples / report
#   6. resources over exactly the scenario window from Prometheus (ab_window.py → resources.json)
#   7. post-run facts: dataset growth, pod restarts, events → run.json (validity flags)
#   8. cool-down
# Order: repeat 1 = A,B; repeat 2 = B,A; … (ABBA cancels slow drift — page cache, thermal, background noise).
# At the end ab_report.py aggregates everything into round-summary.json + REPORT.md (mean ± 95 % CI).
#
# Duration (defaults): per run ≈ reset 1.5 min + warm-up 1.5 min + setup 1–2.5 min + scenario + cool-down 1 min.
# demo:5 ≈ 10 × 13 min ≈ 2 h 10 min; stress:1 ≈ 25 min; capacity:1 ≈ 30 min → default plan ≈ 3 h 05 min.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env
source "$LIB_DIR/lib-bench.sh"

PLAN="demo:5,stress:1,capacity:1"
LABEL="v1"
COOLDOWN_SECONDS="${COOLDOWN_SECONDS:-60}"
RESEED=true
WARMUP=true
ALLOW_MISMATCH=false
TARGETS="ab"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --plan) PLAN="${2:?--plan needs profile:repeats[,profile:repeats…]}"; shift 2 ;;
    --label) LABEL="${2:?--label needs a value}"; shift 2 ;;
    --cooldown) COOLDOWN_SECONDS="${2:?}"; shift 2 ;;
    --no-reseed) RESEED=false; shift ;;
    --no-warmup) WARMUP=false; shift ;;
    --allow-mismatch) ALLOW_MISMATCH=true; shift ;;
    --only) TARGETS="${2:?--only needs a or b}"; shift 2 ;;
    -h|--help) sed -n '2,27p' "$0"; exit 0 ;;
    *) die "Unknown argument: $1 (see --help)" ;;
  esac
done
IFS=',' read -r -a PLAN_ITEMS <<<"$PLAN"
for item in "${PLAN_ITEMS[@]}"; do
  [[ "$item" =~ ^(smoke|demo|stress|capacity):[0-9]+$ ]] || die "bad plan item '$item' (profile:repeats; profiles smoke|demo|stress|capacity)"
done

# --- preflight -------------------------------------------------------------------------------------------------
step "Preflight"
require_cmd k6 "install: https://grafana.com/docs/k6/latest/set-up/install-k6/"
require_cmd kubectl; require_cmd python3; require_cmd curl
mk status >/dev/null 2>&1 || die "minikube profile $MINIKUBE_PROFILE is not running (make mk-up)."
for t in a b; do
  [[ "$TARGETS" == *"$t"* ]] || continue
  ab_kubectl "$t" get deployment "$(ab_deploy "$t")" >/dev/null 2>&1 \
    || die "$(ab_dir "$t") is not deployed (A: make mk-deploy ARGS=--seed-benchmark; B: make gomx-deploy)."
  [[ -d "$(ab_code "$t")/.git" ]] || die "source checkout for $(ab_dir "$t") not found at $(ab_code "$t")."
done
ab_prom_start
ok "Prometheus on :$PROM_LOCAL_PORT"

STAMP="$(date +%Y%m%d-%H%M)"
ROUND="$REPO_ROOT/thesis/results/rounds/$STAMP-$LABEL"
mkdir -p "$ROUND"
LOG="$ROUND/round.log"
exec > >(tee -a "$LOG") 2>&1
info "archive: $ROUND"

cleanup() {
  local rc=$?
  set +e
  step "Cleanup"
  ab_restore_limiter_a && ok "A rate limiter restored (tracing stays off, HPA stays deleted — benchmark shape)"
  ab_prom_stop
  exit "$rc"
}
trap cleanup EXIT

# --- common profile on both sides + round metadata -------------------------------------------------------------
step "Applying the common benchmark profile"
for t in a b; do
  [[ "$TARGETS" == *"$t"* ]] || continue
  ab_apply_profile "$t"
  if ab_check_profile "$t"; then
    ok "$(ab_dir "$t"): profile verified"
  elif $ALLOW_MISMATCH; then
    warn "$(ab_dir "$t"): profile mismatch — continuing (--allow-mismatch, recorded in every run.json)"
  else
    die "$(ab_dir "$t"): live objects deviate from the common profile (see FAIL lines). Fix or pass --allow-mismatch."
  fi
  ab_meta "$t" "$ROUND/meta-$(ab_dir "$t").json"
done
python3 - "$ROUND/round.json" "$PLAN" "$LABEL" "$COOLDOWN_SECONDS" "$RESEED" "$WARMUP" "$AB_REPLICAS" \
  "$(sed -n "s/^export const MIX_VERSION = '\([^']*\)';.*/\1/p" "$REPO_ROOT/load/k6/main.js")" \
  "$(sed -n "s/^export const HARNESS_VERSION = '\([^']*\)';.*/\1/p" "$REPO_ROOT/load/k6/main.js")" <<'PY'
import json, sys, time
out, plan, label, cooldown, reseed, warmup, replicas, mix, harness = sys.argv[1:10]
json.dump({"label": label, "plan": plan, "started_epoch": time.time(), "cooldown_s": int(cooldown),
           "reseed_each_run": reseed == "true", "warmup_each_run": warmup == "true", "replicas": int(replicas),
           "mix_version": mix, "harness_version": harness, "order": "ABBA (odd repeat: A then B; even: B then A)"},
          open(out, "w"), indent=2)
PY

# --- one measured run ------------------------------------------------------------------------------------------
run_one() {
  local t="$1" profile="$2" k="$3" pos="$4" other run_dir before after restarts_before restarts_after rc=0
  other=$([[ "$t" == a ]] && echo b || echo a)
  run_dir="$ROUND/$profile/$(ab_dir "$t")/run-$k"
  mkdir -p "$run_dir"
  step "[$profile $k] $(ab_dir "$t") — $(date +%H:%M:%S)"

  if [[ "$TARGETS" == *"$other"* ]]; then
    ab_scale "$other" 0
    info "$(ab_dir "$other") app scaled to 0 (idle side)"
  fi
  if $RESEED; then
    ab_reseed "$t"
    ok "dataset reset to the Benchmark seed ($AB_EXPECTED_SEED), $AB_REPLICAS fresh pods"
  else
    ab_scale "$t" "$AB_REPLICAS"
  fi
  local profile_ok=true
  ab_check_profile "$t" >"$run_dir/profile-check.txt" || profile_ok=false
  $profile_ok || $ALLOW_MISMATCH || { cat "$run_dir/profile-check.txt"; die "profile drifted before the run."; }

  if $WARMUP; then
    TARGET="$t" RESULTS_DIR="$run_dir/warmup" LOGIN_POOL=2 bash "$LIB_DIR/run-load-test.sh" warmup "$(ab_base "$t")" >/dev/null 2>&1 \
      || warn "warm-up reported a failure (discarded)."
  fi

  before="$(ab_seed_counts "$t")"
  restarts_before="$(ab_kubectl "$t" get pods -o jsonpath='{range .items[*]}{.metadata.name}={.status.containerStatuses[0].restartCount}{" "}{end}')"
  local started; started="$(date +%s)"
  TARGET="$t" RESULTS_DIR="$run_dir" bash "$LIB_DIR/run-load-test.sh" "$profile" "$(ab_base "$t")" || rc=$?
  local finished; finished="$(date +%s)"
  ab_kubectl "$t" get events --sort-by=.lastTimestamp >"$run_dir/events.txt" 2>/dev/null || true
  after="$(ab_seed_counts "$t")"
  restarts_after="$(ab_kubectl "$t" get pods -o jsonpath='{range .items[*]}{.metadata.name}={.status.containerStatuses[0].restartCount}{" "}{end}')"

  if ls "$run_dir"/journeys-*.json >/dev/null 2>&1; then
    ab_window_metrics "$t" "$run_dir" || warn "window metrics failed (Prometheus) — resources.json missing for this run."
  fi
  python3 - "$run_dir/run.json" "$t" "$profile" "$k" "$pos" "$rc" "$started" "$finished" "$before" "$after" \
    "$restarts_before" "$restarts_after" "$profile_ok" <<'PY'
import glob, json, sys
out, t, profile, k, pos, rc, started, finished, before, after, rb, ra, profile_ok = sys.argv[1:14]
def counts(s):
    v = s.split()
    return dict(zip(["users", "threads", "comments", "reactions"], map(int, v))) if len(v) == 4 else None
def restarts(s):
    return {p.split("=")[0]: int(p.split("=")[1] or 0) for p in s.split() if "=" in p}
rb, ra = restarts(rb), restarts(ra)
restarted = sorted(p for p in ra if ra[p] > rb.get(p, ra[p]))
jfiles = sorted(glob.glob(f"{out.rsplit('/', 1)[0]}/journeys-*.json"))
j = json.load(open(jfiles[-1])) if jfiles else {}
totals = j.get("totals", {})
reasons = []
if int(rc) != 0:
    reasons.append(f"k6 exit code {rc}")
if restarted:
    reasons.append(f"pod restarts during the run: {restarted}")
if totals.get("rate_limited"):
    reasons.append(f"{totals['rate_limited']} responses were 429")
if not jfiles:
    reasons.append("no journeys block")
if profile in ("smoke", "demo") and (totals.get("http_req_failed_pct") or 0) > 1:
    reasons.append(f"failed requests {totals['http_req_failed_pct']}% > 1%")
if profile_ok != "true":
    reasons.append("common-profile check failed (--allow-mismatch)")
json.dump({"target": t, "profile": profile, "repeat": int(k), "position_in_pair": int(pos),
           "started_epoch": int(started), "finished_epoch": int(finished),
           "seed_before": counts(before), "seed_after": counts(after),
           "pod_restarts_during_run": restarted, "valid": not reasons, "invalid_reasons": reasons},
          open(out, "w"), indent=2)
print(f"  run.json: valid={not reasons}" + (f" ({'; '.join(reasons)})" if reasons else ""))
PY
  info "cool-down ${COOLDOWN_SECONDS}s"
  sleep "$COOLDOWN_SECONDS"
}

# --- the round -------------------------------------------------------------------------------------------------
for item in "${PLAN_ITEMS[@]}"; do
  profile="${item%%:*}"; repeats="${item##*:}"
  for (( k=1; k<=repeats; k++ )); do
    if (( k % 2 == 1 )); then order=(a b); else order=(b a); fi
    pos=1
    for t in "${order[@]}"; do
      [[ "$TARGETS" == *"$t"* ]] || continue
      run_one "$t" "$profile" "$k" "$pos"
      pos=$((pos + 1))
    done
  done
done

# Leave both apps up for inspection after the round.
for t in a b; do
  [[ "$TARGETS" == *"$t"* ]] && ab_scale "$t" "$AB_REPLICAS"
done

step "Report"
python3 "$LIB_DIR/ab_report.py" "$ROUND" && ok "report: $ROUND/REPORT.md"
ok "round complete: $ROUND"
