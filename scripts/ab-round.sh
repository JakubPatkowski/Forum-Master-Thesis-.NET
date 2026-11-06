#!/usr/bin/env bash
# One complete, unattended A/B MEASUREMENT ROUND (thesis harness v1, 2026-10-17; hardened h6, 2026-10-24). Both
# architectures, every profile of the plan, N repeats each, interleaved ABBA, every run from a freshly reseeded
# dataset — all results, metadata and a generated comparison report archived under
# thesis/results/rounds/<stamp>-<label>/.
#
#   scripts/ab-round.sh                                     # default plan: demo:5,stress:1,capacity:1
#   scripts/ab-round.sh --plan demo:3 --label quick
#   scripts/ab-round.sh --plan smoke:1 --label dryrun        # ~15 min end-to-end check of the whole pipeline
#   make ab-round ARGS="--plan demo:5,capacity:1"
#   scripts/ab-round.sh --plan demo:5 --label v2-auth --auth-reads          # D3: logged-in reads
#   scripts/ab-round.sh --plan demo:5 --label v2-realspa --real-spa         # D1: A replays the real SPA's cold-cache
#                                                                           #     waterfall (B unchanged → "A ideal / A real / B")
#   scripts/ab-round.sh --plan demo:5 --label v2-hpa --hpa                  # A-15: identical HPA 1–3 @ 70 %
#   scripts/ab-round.sh --plan demo:3 --label v2-gc --gc-variant            # D8: workstation GC / GOMEMLIMIT
#   scripts/ab-round.sh --plan demo:3 --label v2-relay --relay-poll-ms 500  # A outbox poll (J8 receipt floor)
#   scripts/ab-round.sh --plan soak:1 --label v2-soak --k6-env SOAK_MINUTES=60
#   scripts/ab-round.sh --resume thesis/results/rounds/<dir> --plan demo:10  # CONTINUE an archive: valid runs are
#                                                                           #   kept, invalid ones redone, new repeats
#                                                                           #   appended (demo:5 tonight + demo:10
#                                                                           #   tomorrow = ONE round with N = 10)
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
#   5. the measured k6 run (run-load-test.sh, bounded by a hard timeout) → summary / journeys (incl. the scenario
#      window) / samples / report
#   6. resources over exactly the scenario window from Prometheus (ab_window.py → resources.json), plus the same
#      pods over an idle window 20–50 s after k6 exited → standing cost per class and marginal CPU per journey;
#      the port-forward is re-established first when it died (a missing resources.json INVALIDATES the run)
#   7. post-run facts: dataset growth vs accepted writes (comments, threads, DMs — "acknowledged but not
#      persisted" invalidates), failed checks, pod restarts, events → run.json (validity flags)
#   8. cool-down
# Order: repeat 1 = A,B; repeat 2 = B,A; … (ABBA cancels slow drift — page cache, thermal, background noise).
# At the end ab_report.py aggregates everything into round-summary.json + REPORT.md (mean ± 95 % CI, median [IQR],
# Mann-Whitney/Holm verdicts).
#
# Watching it: <round>/status.json (state RUNNING|DONE|FAILED, current run, progress, heartbeat every 60 s) and
# thesis/results/rounds/LATEST (path of the newest round) — `make ab-status` prints both; a RUNNING state whose
# heartbeat is older than ~15 min means the process died (a Claude/terminal session that owned it ended → start
# rounds with `setsid nohup … &`) — resume with --resume. Every external step is bounded (k6 timeout per profile,
# kubectl waits, psql execs), so a hang ends as an INVALID run, never as an endless wait.
#
# Duration (measured in round v1): per run ≈ 12.2 min demo / 11.6 stress / 13.2 capacity (incl. reseed, warm-up,
# setup, idle window) → demo:10 ≈ 4 h, stress:3 ≈ 1 h 10, capacity:3 ≈ 1 h 20; the default plan ≈ 2 h 30.
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
K6_ENV=()
RESUME_DIR=""
# Variants (harness h4) — see lib-bench.sh: --hpa, --gc-variant, --relay-poll-ms N, --auth-reads, --real-spa, plus any
# --k6-env KEY=VALUE forwarded to k6 (e.g. WS_PROBE_INTERVAL_MS=5000, SOAK_MINUTES=90, REAL_SPA_BATCH_PER_HOST=64).
while [[ $# -gt 0 ]]; do
  case "$1" in
    --plan) PLAN="${2:?--plan needs profile:repeats[,profile:repeats…]}"; shift 2 ;;
    --label) LABEL="${2:?--label needs a value}"; shift 2 ;;
    --cooldown) COOLDOWN_SECONDS="${2:?}"; shift 2 ;;
    --no-reseed) RESEED=false; shift ;;
    --no-warmup) WARMUP=false; shift ;;
    --allow-mismatch) ALLOW_MISMATCH=true; shift ;;
    --only) TARGETS="${2:?--only needs a or b}"; shift 2 ;;
    --hpa) AB_HPA=true; shift ;;
    --gc-variant) AB_GC_VARIANT=true; shift ;;
    --relay-poll-ms) AB_RELAY_POLL_MS="${2:?--relay-poll-ms needs milliseconds}"; shift 2 ;;
    --auth-reads) AB_AUTH_READS=true; shift ;;
    --real-spa) AB_REAL_SPA=true; shift ;;
    --k6-env) K6_ENV+=("${2:?--k6-env needs KEY=VALUE}"); shift 2 ;;
    --resume) RESUME_DIR="${2:?--resume needs an existing round directory}"; shift 2 ;;
    -h|--help) sed -n '2,48p' "$0"; exit 0 ;;
    *) die "Unknown argument: $1 (see --help)" ;;
  esac
done
IFS=',' read -r -a PLAN_ITEMS <<<"$PLAN"
for item in "${PLAN_ITEMS[@]}"; do
  [[ "$item" =~ ^(smoke|demo|stress|capacity|soak|spike):[0-9]+$ ]] || die "bad plan item '$item' (profile:repeats; profiles smoke|demo|stress|capacity|soak|spike)"
done
# Variant env for k6 (main.js records them in the journeys block; run-load-test.sh forwards them).
[[ "$AB_AUTH_READS" == true ]] && export AUTH_READS=1
[[ "$AB_REAL_SPA" == true ]] && export REAL_SPA=1   # targets/a.js + main.js read it; the B adapter ignores it
for kv in "${K6_ENV[@]}"; do export "${kv?}"; done

# --- preflight -------------------------------------------------------------------------------------------------
step "Preflight"
require_cmd k6 "install: https://grafana.com/docs/k6/latest/set-up/install-k6/"
require_cmd kubectl; require_cmd python3; require_cmd curl; require_cmd timeout
mk status >/dev/null 2>&1 || die "minikube profile $MINIKUBE_PROFILE is not running (make mk-up)."
if pgrep -f "scripts/ab-round.sh" | grep -qv "^$$\$"; then
  # Two rounds would fight over the same cluster (scale-to-0, limiter, port-forward port).
  others="$(pgrep -f "scripts/ab-round.sh" | grep -v "^$$\$" | tr '\n' ' ')"
  die "another ab-round.sh is running (pid $others) — wait for it or stop it (make ab-status)."
fi
for t in a b; do
  [[ "$TARGETS" == *"$t"* ]] || continue
  ab_kubectl "$t" get deployment "$(ab_deploy "$t")" >/dev/null 2>&1 \
    || die "$(ab_dir "$t") is not deployed (A: make mk-deploy ARGS=--seed-benchmark; B: make gomx-deploy)."
  [[ -d "$(ab_code "$t")/.git" ]] || die "source checkout for $(ab_dir "$t") not found at $(ab_code "$t")."
done
ab_prom_start
ok "Prometheus on :$PROM_LOCAL_PORT"

STAMP="$(date +%Y%m%d-%H%M)"
RESUMING=false
if [[ -n "$RESUME_DIR" ]]; then
  ROUND="$(cd "$RESUME_DIR" 2>/dev/null && pwd)" || die "--resume: $RESUME_DIR does not exist"
  [[ -f "$ROUND/round.json" ]] || die "--resume: $ROUND has no round.json (not a round archive)"
  LABEL="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["label"])' "$ROUND/round.json")"
  RESUMING=true
else
  ROUND="$REPO_ROOT/thesis/results/rounds/$STAMP-$LABEL"
  mkdir -p "$ROUND"
fi
LOG="$ROUND/round.log"
exec > >(tee -a "$LOG") 2>&1
info "archive: $ROUND${RESUMING:+ (resumed $STAMP)}"

# --- status file + heartbeat -----------------------------------------------------------------------------------
STATUS="$ROUND/status.json"
RUNS_TOTAL=0; RUNS_DONE=0; RUNS_VALID=0; CURRENT=""
ab_status() { # state [detail]
  python3 - "$STATUS" "$1" "${2:-}" "$CURRENT" "$RUNS_DONE" "$RUNS_TOTAL" "$RUNS_VALID" "$ROUND" "$$" "$PLAN" <<'PY'
import json, os, sys, time
out, state, detail, current, done, total, valid, rnd, pid, plan = sys.argv[1:11]
now = int(time.time())
doc = {"state": state, "detail": detail, "plan": plan, "current_run": current, "runs_done": int(done),
       "runs_total": int(total), "runs_valid": int(valid), "round_dir": rnd, "pid": int(pid),
       "updated_epoch": now, "heartbeat_epoch": now, "updated": time.strftime("%Y-%m-%d %H:%M:%S")}
tmp = out + ".tmp"
json.dump(doc, open(tmp, "w"), indent=2)
os.replace(tmp, out)
PY
  echo "$ROUND" >"$REPO_ROOT/thesis/results/rounds/LATEST"
}
heartbeat_loop() {
  while :; do
    sleep 60
    python3 - "$STATUS" <<'PY' 2>/dev/null || true
import json, os, sys, time
p = sys.argv[1]
try:
    d = json.load(open(p))
except Exception:
    sys.exit(0)
d["heartbeat_epoch"] = int(time.time())
tmp = p + ".tmp"
json.dump(d, open(tmp, "w"), indent=2)
os.replace(tmp, p)
PY
  done
}
heartbeat_loop &
HB_PID=$!
ab_status RUNNING "preflight"

cleanup() {
  local rc=$?
  set +e
  step "Cleanup"
  kill "$HB_PID" 2>/dev/null || true
  ab_restore_limiter_a && ok "A rate limiter restored (tracing stays off, HPA stays deleted — benchmark shape)"
  ab_prom_stop
  if (( rc == 0 )); then
    ab_status DONE "round complete: $RUNS_VALID/$RUNS_TOTAL valid"
  else
    ab_status FAILED "exit $rc after $RUNS_DONE/$RUNS_TOTAL runs — see round.log; continue with --resume $ROUND"
  fi
  exit "$rc"
}
trap cleanup EXIT

# --- common profile on both sides + round metadata -------------------------------------------------------------
step "Applying the common benchmark profile"
ab_status RUNNING "applying the common profile"
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
  if $RESUMING && [[ -f "$ROUND/meta-$(ab_dir "$t").json" ]]; then
    # Keep the original environment record; write the resumed state beside it and refuse a silently different image.
    ab_meta "$t" "$ROUND/meta-$(ab_dir "$t").resume-$STAMP.json"
    orig="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1])).get("deployed_image",""))' "$ROUND/meta-$(ab_dir "$t").json")"
    now="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1])).get("deployed_image",""))' "$ROUND/meta-$(ab_dir "$t").resume-$STAMP.json")"
    [[ "$orig" == "$now" ]] || die "--resume: $(ab_dir "$t") image changed ($orig → $now) — a resumed round must measure the same build."
  else
    ab_meta "$t" "$ROUND/meta-$(ab_dir "$t").json"
  fi
done
python3 - "$ROUND/round.json" "$PLAN" "$LABEL" "$COOLDOWN_SECONDS" "$RESEED" "$WARMUP" "$AB_REPLICAS" \
  "$(sed -n "s/^export const MIX_VERSION = '\([^']*\)';.*/\1/p" "$REPO_ROOT/load/k6/main.js")" \
  "$(sed -n "s/^export const HARNESS_VERSION = '\([^']*\)';.*/\1/p" "$REPO_ROOT/load/k6/main.js")" \
  "$AB_HPA" "$AB_GC_VARIANT" "$AB_RELAY_POLL_MS" "$AB_AUTH_READS" "$AB_REAL_SPA" "${K6_ENV[*]}" "$RESUMING" <<'PY'
import json, os, sys, time
out, plan, label, cooldown, reseed, warmup, replicas, mix, harness = sys.argv[1:10]
hpa, gc, relay, auth, real_spa, k6env, resuming = sys.argv[10:17]
doc = {"label": label, "plan": plan, "started_epoch": time.time(), "cooldown_s": int(cooldown),
       "reseed_each_run": reseed == "true", "warmup_each_run": warmup == "true", "replicas": int(replicas),
       "mix_version": mix, "harness_version": harness, "order": "ABBA (odd repeat: A then B; even: B then A)",
       "variants": {"hpa": hpa == "true", "gc_variant": gc == "true", "relay_poll_ms": int(relay) if relay else None,
                    "auth_reads": auth == "true", "real_spa": real_spa == "true",
                    "k6_env": k6env.split() if k6env else []}}
if resuming == "true" and os.path.exists(out):
    old = json.load(open(out))
    if old.get("harness_version") != harness or old.get("mix_version") != mix:
        sys.exit(f"--resume: harness/mix changed ({old.get('harness_version')}/{old.get('mix_version')} → {harness}/{mix}) — start a new round instead.")
    if old.get("variants") != doc["variants"]:
        sys.exit(f"--resume: variant flags differ from the archive's ({old.get('variants')}) — pass the same flags.")
    old["plan"] = plan  # the plan may grow (demo:5 → demo:10); the archive's history keeps every attempt
    old.setdefault("resumed_epochs", []).append(time.time())
    old.setdefault("plans", [old.get("plan")]).append(plan)
    doc = old
json.dump(doc, open(out, "w"), indent=2)
PY

# --- one measured run ------------------------------------------------------------------------------------------
run_is_valid() { python3 -c 'import json,sys; sys.exit(0 if json.load(open(sys.argv[1])).get("valid") else 1)' "$1" 2>/dev/null; }

run_one() {
  local t="$1" profile="$2" k="$3" pos="$4" other run_dir before after restarts_before restarts_after rc=0
  other=$([[ "$t" == a ]] && echo b || echo a)
  run_dir="$ROUND/$profile/$(ab_dir "$t")/run-$k"
  CURRENT="$profile/$(ab_dir "$t")/run-$k"
  if [[ -f "$run_dir/run.json" ]]; then
    if run_is_valid "$run_dir/run.json"; then
      info "[$profile $k] $(ab_dir "$t") — valid in the archive, kept (--resume)"
      RUNS_DONE=$((RUNS_DONE + 1)); RUNS_VALID=$((RUNS_VALID + 1))
      ab_status RUNNING "kept $CURRENT (already valid)"
      return 0
    fi
    # Redo an invalid attempt; keep its artefacts outside the report's glob (<profile>/<side>/run-*/).
    local attic="$ROUND/invalid-attempts/$profile-$(ab_dir "$t")-run-$k-$(date +%Y%m%d-%H%M%S)"
    mkdir -p "$(dirname "$attic")"
    mv "$run_dir" "$attic"
    info "[$profile $k] $(ab_dir "$t") — previous attempt invalid, moved to invalid-attempts/, redoing"
  fi
  mkdir -p "$run_dir"
  step "[$profile $k] $(ab_dir "$t") — $(date +%H:%M:%S)"
  ab_status RUNNING "reseeding before $CURRENT"

  if [[ "$TARGETS" == *"$other"* ]]; then
    ab_scale "$other" 0
    info "$(ab_dir "$other") app scaled to 0 (idle side)"
  fi
  if $RESEED; then
    ab_reseed "$t"
    ok "dataset reset to the Benchmark seed ($AB_EXPECTED_SEED) + ANALYZE, $(ab_start_replicas) fresh pod(s)"
  else
    ab_scale "$t" "$(ab_start_replicas)"
  fi
  local profile_ok=true
  ab_check_profile "$t" >"$run_dir/profile-check.txt" || profile_ok=false
  $profile_ok || $ALLOW_MISMATCH || { cat "$run_dir/profile-check.txt"; die "profile drifted before the run."; }

  if $WARMUP; then
    ab_status RUNNING "warm-up before $CURRENT"
    TARGET="$t" RESULTS_DIR="$run_dir/warmup" LOGIN_POOL=2 bash "$LIB_DIR/run-load-test.sh" warmup "$(ab_base "$t")" >/dev/null 2>&1 \
      || warn "warm-up reported a failure (discarded)."
  fi

  before="$(ab_seed_counts "$t")"
  restarts_before="$(ab_kubectl "$t" get pods -o jsonpath='{range .items[*]}{.metadata.name}={.status.containerStatuses[0].restartCount}{" "}{end}')"
  ab_status RUNNING "k6 $profile running: $CURRENT"
  local started; started="$(date +%s)"
  TARGET="$t" RESULTS_DIR="$run_dir" bash "$LIB_DIR/run-load-test.sh" "$profile" "$(ab_base "$t")" || rc=$?
  local finished; finished="$(date +%s)"
  ab_kubectl "$t" get events --sort-by=.lastTimestamp >"$run_dir/events.txt" 2>/dev/null || true
  after="$(ab_seed_counts "$t")"
  restarts_after="$(ab_kubectl "$t" get pods -o jsonpath='{range .items[*]}{.metadata.name}={.status.containerStatuses[0].restartCount}{" "}{end}')"

  local resources_ok=false
  if ls "$run_dir"/journeys-*.json >/dev/null 2>&1; then
    # Idle (no-traffic) window 20–50 s after k6 exited: same pods, nothing to serve → the standing cost of each
    # class (A: RabbitMQ + relays, B: River polling) and the marginal CPU per journey. The wait for it (~95 s after
    # the run, cAdvisor scrape lag included) replaces most of the cool-down.
    ab_status RUNNING "reading the Prometheus windows of $CURRENT"
    if ab_window_metrics "$t" "$run_dir" "$(( (finished + 20) * 1000 ))" "$(( (finished + 50) * 1000 ))"; then
      [[ -s "$run_dir/resources.json" ]] && resources_ok=true
    else
      warn "window metrics failed (Prometheus) — resources.json missing for this run (run marked invalid)."
    fi
  fi
  python3 - "$run_dir/run.json" "$t" "$profile" "$k" "$pos" "$rc" "$started" "$finished" "$before" "$after" \
    "$restarts_before" "$restarts_after" "$profile_ok" "$resources_ok" <<'PY'
import glob, json, statistics, sys
out, t, profile, k, pos, rc, started, finished, before, after, rb, ra, profile_ok, resources_ok = sys.argv[1:15]
def counts(s):
    v = s.split()
    keys = ["users", "threads", "comments", "reactions", "messages"]
    return dict(zip(keys, map(int, v))) if len(v) in (4, 5) else None
def restarts(s):
    return {p.split("=")[0]: int(p.split("=")[1] or 0) for p in s.split() if "=" in p}
rb, ra = restarts(rb), restarts(ra)
restarted = sorted(p for p in ra if ra[p] > rb.get(p, ra[p]))
jfiles = sorted(glob.glob(f"{out.rsplit('/', 1)[0]}/journeys-*.json"))
j = json.load(open(jfiles[-1])) if jfiles else {}
totals = j.get("totals", {})
endpoints = j.get("endpoints") or {}
reasons = []
if int(rc) != 0:
    reasons.append(f"k6 exit code {rc}")
if restarted:
    reasons.append(f"pod restarts during the run: {restarted}")
if totals.get("rate_limited"):
    reasons.append(f"{totals['rate_limited']} responses were 429")
if not jfiles:
    reasons.append("no journeys block")
# Pass/fail profiles: both k6's own failure rate (status ≥ 400) and the harness checks (semantic failures that come
# back as 2xx/3xx — B's redirect-on-rejection, synthetic 598/599) must stay ≤ 1 %. Knee-finding profiles only record.
if profile in ("smoke", "demo", "soak"):
    if (totals.get("http_req_failed_pct") or 0) > 1:
        reasons.append(f"failed requests {totals['http_req_failed_pct']}% > 1%")
    if (totals.get("checks_failed_pct") or 0) > 1:
        reasons.append(f"failed checks {totals['checks_failed_pct']}% > 1% ({totals.get('checks_failed')} rejected/failed operations)")
# Acknowledged writes must exist as rows: the dataset delta per table vs the accepted count of the endpoint(s) that
# write it (lib/track.js forum_check_ok). A shortfall > 1 % = the server said yes and stored nothing (or the
# adapter's success rule is wrong) — either way not a number to publish.
sb, sa = counts(before), counts(after)
def accepted(*names):
    return sum((endpoints.get(n) or {}).get("ok", 0) for n in names)
write_check = {}
if sb and sa and endpoints:
    for table, want in (("comments", accepted("comment_create", "probe_comment")), ("threads", accepted("thread_create")),
                        ("messages", accepted("dm_send"))):
        if table in sa and table in sb:
            got = sa[table] - sb[table]
            write_check[table] = {"accepted": want, "new_rows": got}
            if want > 0 and got < 0.99 * want:
                reasons.append(f"{table}: {got} new rows for {want} accepted writes (acknowledged but not persisted)")
if resources_ok != "true":
    reasons.append("no resources.json (Prometheus window read failed) — H1 data missing")
if profile_ok != "true":
    reasons.append("common-profile check failed (--allow-mismatch)")
# Generator sanity gate (§11.2): k6 mean CPU <= 1 core (100%), peak <= 2 cores (200%)
sfiles = sorted(glob.glob(f"{out.rsplit('/', 1)[0]}/samples-*.json"))
if sfiles:
    try:
        samples = json.load(open(sfiles[-1]))
        pts = [(s.get("epoch"), s.get("k6_cpu_ticks"), s.get("clk_tck") or 100) for s in samples]
        pts = [p for p in pts if p[0] is not None and p[1] is not None]
        pct = []
        for (e0, t0, hz), (e1, t1, _) in zip(pts, pts[1:]):
            if e1 > e0 and t1 >= t0:
                pct.append((t1 - t0) / hz / (e1 - e0) * 100)
        if pct:
            k6_mean, k6_max = statistics.mean(pct), max(pct)
            if k6_mean > 100:
                reasons.append(f"generator saturated: k6 mean CPU {k6_mean:.1f}% > 100% (1 core)")
            elif k6_max > 200:
                reasons.append(f"generator spike: k6 peak CPU {k6_max:.1f}% > 200% (2 cores)")
    except Exception:
        pass
json.dump({"target": t, "profile": profile, "repeat": int(k), "position_in_pair": int(pos),
           "started_epoch": int(started), "finished_epoch": int(finished),
           "seed_before": sb, "seed_after": sa, "write_check": write_check,
           "checks_failed": totals.get("checks_failed"), "checks_failed_pct": totals.get("checks_failed_pct"),
           "pod_restarts_during_run": restarted, "valid": not reasons, "invalid_reasons": reasons},
          open(out, "w"), indent=2)
print(f"  run.json: valid={not reasons}" + (f" ({'; '.join(reasons)})" if reasons else ""))
PY
  RUNS_DONE=$((RUNS_DONE + 1))
  if run_is_valid "$run_dir/run.json"; then RUNS_VALID=$((RUNS_VALID + 1)); fi
  ab_status RUNNING "finished $CURRENT ($RUNS_VALID/$RUNS_DONE valid so far)"
  local remaining=$(( COOLDOWN_SECONDS - ($(date +%s) - finished) ))
  (( remaining > 0 )) || remaining=0
  info "cool-down ${remaining}s (of ${COOLDOWN_SECONDS}s; the idle-window read already waited)"
  sleep "$remaining"
}

# --- the round -------------------------------------------------------------------------------------------------
for item in "${PLAN_ITEMS[@]}"; do
  repeats="${item##*:}"
  for t in a b; do [[ "$TARGETS" == *"$t"* ]] && RUNS_TOTAL=$((RUNS_TOTAL + repeats)); done
done
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
  [[ "$TARGETS" == *"$t"* ]] && ab_scale "$t" "$(ab_start_replicas)"
done

step "Report"
ab_status RUNNING "generating the report"
python3 "$LIB_DIR/ab_report.py" "$ROUND" && ok "report: $ROUND/REPORT.md"
ok "round complete: $ROUND ($RUNS_VALID/$RUNS_TOTAL valid)"
