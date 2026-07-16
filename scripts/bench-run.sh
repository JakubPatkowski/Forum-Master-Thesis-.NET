#!/usr/bin/env bash
# Phase 9c — the measured-benchmark orchestrator. Produces the thesis numbers for Architecture A.
#
#   scripts/bench-run.sh [demo|stress] [--repeats N] [--skip-warmup]
#
# Flow: preflight (cluster + monitoring + Benchmark seed present) → record meta.json → raise the
# per-IP rate limiter (all k6 traffic shares ONE host IP; restored afterwards, ALWAYS — trap) →
# one discarded smoke warm-up (JIT/pools/caches) → N repeats of the profile with 2 min cool-downs →
# Prometheus range snapshots of the §10c dashboard queries over the whole window → mean±stddev.
#
# Everything lands in thesis/results/A/<stamp>-<profile>/:
#   meta.json                run metadata: git SHA, image digest, node stats, limiter values, seed counts
#   warmup/                  the discarded warm-up artifacts (kept for honesty, never analyzed)
#   run-1..N/                summary-*.json + samples-*.json + events.txt per repeat
#   prometheus-snapshots.json  query_range results for the dashboard queries (15 s step)
#   stats.json               mean ± stddev of req/s and p95 across the N repeats
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env

PROFILE=demo
REPEATS=3
WARMUP=true
while [[ $# -gt 0 ]]; do
  case "$1" in
    demo|stress) PROFILE="$1"; shift ;;
    smoke) die "smoke is the warm-up profile, not a measured one (use demo|stress)" ;;
    --repeats) REPEATS="${2:?--repeats needs a number}"; shift 2 ;;
    --skip-warmup) WARMUP=false; shift ;;
    *) die "Unknown argument: $1 (usage: bench-run.sh [demo|stress] [--repeats N] [--skip-warmup])" ;;
  esac
done

BASE_URL="${BASE_URL:-https://forum.local}"
PROM_LOCAL_PORT="${PROM_LOCAL_PORT:-19091}"   # NOT 19090 — that one belongs to dev-tunnels
COOLDOWN_SECONDS="${COOLDOWN_SECONDS:-120}"

# --- 1. preflight -------------------------------------------------------------------------------
step "Preflight"
require_cmd k6 "install: https://grafana.com/docs/k6/latest/set-up/install-k6/"
require_cmd kubectl
require_cmd python3
kc get deployment backend >/dev/null 2>&1 || die "backend deployment not found — deploy first (make mk-deploy)."
kc rollout status deployment/backend --timeout=60s >/dev/null || die "backend not ready."
kubectl -n "$MONITORING_NAMESPACE" get svc monitoring-kube-prometheus-prometheus >/dev/null 2>&1 \
  || die "monitoring stack not found — the measured run needs Prometheus snapshots (make mon-up)."

# Completed migrate/seed Job pods carry `app: backend` (postgres NetworkPolicy) which ALSO matches the backend
# Deployment/HPA selector — a lingering terminated pod has no metrics, so the HPA reports "did not receive metrics
# for targeted pods" and refuses to scale up, silently flat-lining the demo staircase (found live in 9c). The Job
# manifests now self-clean via ttlSecondsAfterFinished, but a reseed right before the bench can leave one inside its
# ttl window — sweep any terminated app=backend pods now (never touches Running Deployment pods).
STALE_JOB_PODS="$(kc get pods -l app=backend --field-selector=status.phase=Succeeded -o name 2>/dev/null; kc get pods -l app=backend --field-selector=status.phase=Failed -o name 2>/dev/null)"
if [[ -n "$STALE_JOB_PODS" ]]; then
  step "Removing terminated app=backend Job pods (they pollute the HPA metric pool)"
  echo "$STALE_JOB_PODS" | xargs -r kc delete >/dev/null 2>&1 || true
fi

# Seed sentinel: the cluster runs ONE database (the --benchmark k8s Job reseeds it in place, unlike the
# local-compose forum_net_bench split). Benchmark profile seeds 800 users; Development only 5.
CLUSTER_DB="$(kc get secret postgres-credentials -o jsonpath='{.data.POSTGRES_DB}' | base64 -d)"
USER_COUNT="$(kc exec postgres-0 -- psql -U "$POSTGRES_USER" -d "$CLUSTER_DB" -tAc 'SELECT count(*) FROM forum_identity.users' 2>/dev/null | tr -d '[:space:]')"
[[ "$USER_COUNT" =~ ^[0-9]+$ ]] || die "Could not count users in '$CLUSTER_DB' — is Postgres up?"
(( USER_COUNT >= 100 )) || die "Only $USER_COUNT users in '$CLUSTER_DB' — not the Benchmark seed. Run: make seed ARGS='--benchmark --cluster'"
# Scalar subqueries in ONE row: UNION ALL branch order is not guaranteed (parallel Append) — found live.
SEED_COUNTS="$(kc exec postgres-0 -- psql -U "$POSTGRES_USER" -d "$CLUSTER_DB" -tAc \
  "SELECT (SELECT count(*) FROM forum_identity.users) || ' ' ||
          (SELECT count(*) FROM forum_content.threads) || ' ' ||
          (SELECT count(*) FROM forum_content.comments) || ' ' ||
          (SELECT count(*) FROM forum_engagement.reactions)")"
ok "Benchmark seed present in '$CLUSTER_DB' (users/threads/comments/reactions: $SEED_COUNTS)"

STAMP="$(date +%Y%m%d-%H%M)"
ARCHIVE="$REPO_ROOT/thesis/results/A/$STAMP-$PROFILE"
mkdir -p "$ARCHIVE"

# --- 2. meta.json (every thesis number maps to an exact build + environment) ---------------------
step "Recording run metadata"
DEPLOYED_IMAGE="$(kc get deployment backend -o jsonpath='{.spec.template.spec.containers[0].image}')"
IMAGE_DIGEST="$(kc get pods -l app=backend -o jsonpath='{.items[0].status.containerStatuses[0].imageID}')"
GIT_SHA="$(git -C "$REPO_ROOT" rev-parse HEAD)"
GIT_DIRTY=false; [[ -n "$(git -C "$REPO_ROOT" status --porcelain)" ]] && GIT_DIRTY=true
ORIG_GLOBAL_OVERRIDE="$(kc get deployment backend -o jsonpath='{.spec.template.spec.containers[0].env[?(@.name=="RateLimiting__Global__PermitLimit")].value}')"
ORIG_AUTH_OVERRIDE="$(kc get deployment backend -o jsonpath='{.spec.template.spec.containers[0].env[?(@.name=="RateLimiting__Auth__PermitLimit")].value}')"
CONFIGMAP_GLOBAL="$(kc get configmap backend-config -o jsonpath='{.data.RateLimiting__Global__PermitLimit}' 2>/dev/null || echo 100)"
CONFIGMAP_AUTH="$(kc get configmap backend-config -o jsonpath='{.data.RateLimiting__Auth__PermitLimit}' 2>/dev/null || echo 10)"

python3 - "$ARCHIVE/meta.json" <<EOF
import json, subprocess, sys
def sh(cmd):
    try: return subprocess.run(cmd, shell=True, capture_output=True, text=True, timeout=30).stdout.strip()
    except Exception: return ""
seed = "$SEED_COUNTS".split()
meta = {
    "architecture": "A (React SPA + .NET 10 modular monolith)",
    "profile": "$PROFILE",
    "repeats": $REPEATS,
    "started_utc": sh("date -u +%Y-%m-%dT%H:%M:%SZ"),
    "base_url": "$BASE_URL",
    "git_sha": "$GIT_SHA",
    "git_dirty": "$GIT_DIRTY" == "true",
    "deployed_image": "$DEPLOYED_IMAGE",
    "image_digest": "$IMAGE_DIGEST",
    "k6_version": sh("k6 version"),
    "seed": {"database": "$CLUSTER_DB",
             "users": int(seed[0]), "threads": int(seed[1]), "comments": int(seed[2]), "reactions": int(seed[3])},
    "rate_limiter": {
        # The raise is required and RECORDED: all k6 traffic egresses ONE host IP, so the per-IP
        # production posture would 429 the benchmark itself (plan §9c / G21). Auth is raised too —
        # setup() logs in 200 users from that same single IP, which no per-IP window models fairly.
        "raised_for_run": {"RateLimiting__Global__PermitLimit": "1000000", "RateLimiting__Auth__PermitLimit": "1000000"},
        "restored_to": {"global_env_override": "$ORIG_GLOBAL_OVERRIDE" or None, "auth_env_override": "$ORIG_AUTH_OVERRIDE" or None,
                        "configmap_global": "$CONFIGMAP_GLOBAL", "configmap_auth": "$CONFIGMAP_AUTH"},
    },
    "node": {"kubectl_nodes": sh("kubectl get nodes -o wide --no-headers"),
             "kubectl_top_node": sh("kubectl top nodes --no-headers"),
             "minikube_vm": sh("docker inspect $MINIKUBE_PROFILE --format '{{.HostConfig.NanoCpus}} nanocpus / {{.HostConfig.Memory}} bytes'"),
             "wsl_kernel": sh("uname -r"),
             "wsl_mem_mb": sh("free -m | awk '/^Mem:/{print \$2}'")},
    "k6_host": "WSL host, outside the cluster, through ingress-nginx (plan §9c design decision)",
}
json.dump(meta, open(sys.argv[1], "w"), indent=2)
EOF
ok "meta.json written"

# --- 3. raise the rate limiter (restored by trap, even on failure) -------------------------------
restore_limiter() {
  step "Restoring rate limiter"
  if [[ -n "$ORIG_GLOBAL_OVERRIDE" || -n "$ORIG_AUTH_OVERRIDE" ]]; then
    # There were explicit overrides before us — put them back verbatim.
    kc set env deployment/backend \
      "RateLimiting__Global__PermitLimit=${ORIG_GLOBAL_OVERRIDE:-$CONFIGMAP_GLOBAL}" \
      "RateLimiting__Auth__PermitLimit=${ORIG_AUTH_OVERRIDE:-$CONFIGMAP_AUTH}" >/dev/null
  else
    # No overrides existed — remove ours so the configmap values apply again.
    kc set env deployment/backend RateLimiting__Global__PermitLimit- RateLimiting__Auth__PermitLimit- >/dev/null
  fi
  kc rollout status deployment/backend --timeout=180s >/dev/null || warn "rollout after limiter restore did not settle."
  ok "limiter restored (configmap: global=$CONFIGMAP_GLOBAL auth=$CONFIGMAP_AUTH)"
}
step "Raising rate limiter for the measured run"
kc set env deployment/backend RateLimiting__Global__PermitLimit=1000000 RateLimiting__Auth__PermitLimit=1000000 >/dev/null
trap restore_limiter EXIT
kc rollout status deployment/backend --timeout=180s >/dev/null || die "rollout after limiter raise failed."
LIMITER_ROLLOUT_EPOCH="$(date +%s)"
ok "PermitLimit=1000000 (global+auth) live"

BENCH_START_EPOCH="$(date +%s)"

# --- 4. warm-up (discarded: primes JIT, connection pools, buffer cache) --------------------------
if $WARMUP; then
  step "Warm-up (smoke, discarded)"
  mkdir -p "$ARCHIVE/warmup"
  RESULTS_DIR="$ARCHIVE/warmup" bash "$LIB_DIR/run-load-test.sh" smoke "$BASE_URL" || warn "warm-up reported failure — continuing (it is discarded)."
fi

# HPA CPU-initialization guard (found live in 9c): kube-controller-manager ignores the CPU of pods
# younger than ~5 min (--horizontal-pod-autoscaler-cpu-initialization-period) when computing scale-up,
# and the limiter raise above just rolled every backend pod. A ramp that starts before the pods age
# past that window sees a delayed/absent HPA staircase. The warm-up usually eats most of the wait.
HPA_GRACE_REMAINING=$(( ${HPA_CPU_INIT_GRACE:-300} - ($(date +%s) - LIMITER_ROLLOUT_EPOCH) ))
if (( HPA_GRACE_REMAINING > 0 )); then
  info "waiting ${HPA_GRACE_REMAINING}s for backend pods to age past the HPA CPU-initialization period"
  sleep "$HPA_GRACE_REMAINING"
fi

# --- 5. N measured repeats with cool-downs -------------------------------------------------------
for (( i=1; i<=REPEATS; i++ )); do
  step "Measured run $i/$REPEATS ($PROFILE)"
  RUN_DIR="$ARCHIVE/run-$i"
  mkdir -p "$RUN_DIR"
  # run-load-test.sh already maps stress threshold breaches to exit 0 (informational by design),
  # so a non-zero here is a real crash — tolerated only for stress (partial knee data still counts).
  RESULTS_DIR="$RUN_DIR" bash "$LIB_DIR/run-load-test.sh" "$PROFILE" "$BASE_URL" \
    || { [[ "$PROFILE" == "stress" ]] && warn "stress run $i failed (rc≠0) — continuing with partial data." \
         || die "measured run $i failed — archive is incomplete, aborting."; }
  kc get events --sort-by=.lastTimestamp >"$RUN_DIR/events.txt" 2>/dev/null || true
  if (( i < REPEATS )); then
    info "cool-down ${COOLDOWN_SECONDS}s (drains HPA scale-down + outbox backlog before the next repeat)"
    sleep "$COOLDOWN_SECONDS"
  fi
done
BENCH_END_EPOCH="$(date +%s)"

# --- 6. Prometheus range snapshots over the whole window (§10c dashboard queries) ----------------
step "Prometheus snapshots"
kubectl -n "$MONITORING_NAMESPACE" port-forward svc/monitoring-kube-prometheus-prometheus "$PROM_LOCAL_PORT:9090" >/dev/null 2>&1 &
PF_PID=$!
sleep 3
python3 - "$ARCHIVE/prometheus-snapshots.json" <<EOF || warn "Prometheus snapshot failed — dashboards remain the fallback."
import json, sys, urllib.parse, urllib.request
# Queries lifted from k8s/monitoring/QUERIES.md (the extract of the §10c dashboards) with
# \$__rate_interval replaced by a literal [1m], as that file's header instructs.
queries = {
    "rps":            'sum(rate(http_server_request_duration_seconds_count{job="backend"}[1m]))',
    "p95_ms":         'histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket{job="backend", http_route!="/api/realtime/ws"}[1m]))) * 1000',
    "p99_ms":         'histogram_quantile(0.99, sum by (le) (rate(http_server_request_duration_seconds_bucket{job="backend", http_route!="/api/realtime/ws"}[1m]))) * 1000',
    "rps_by_route":   'sum by (http_route) (rate(http_server_request_duration_seconds_count{job="backend"}[1m]))',
    "p95_by_route_ms":'histogram_quantile(0.95, sum by (le, http_route) (rate(http_server_request_duration_seconds_bucket{job="backend", http_route!="/api/realtime/ws"}[5m]))) * 1000',
    "errors_5xx":     'sum(rate(http_server_request_duration_seconds_count{job="backend", http_response_status_code=~"5.."}[1m])) or vector(0)',
    "hpa_replicas":   'kube_horizontalpodautoscaler_status_current_replicas{horizontalpodautoscaler="backend"}',
    "hpa_desired":    'kube_horizontalpodautoscaler_status_desired_replicas{horizontalpodautoscaler="backend"}',
    "backend_cpu_cores":   'sum by (pod) (rate(container_cpu_usage_seconds_total{namespace="forum-dotnet", pod=~"backend-.*", container=""}[2m]))',
    "backend_mem_bytes":   'sum by (pod) (container_memory_working_set_bytes{namespace="forum-dotnet", pod=~"backend-.*", container=""})',
    "pg_connections": 'sum(pg_stat_activity_count)',
    "rabbit_poison":  'sum(rabbitmq_queue_messages_ready{queue=~".*poison"}) or vector(0)',  # unescaped dot: robust through heredoc→URL
    "ws_connections": 'forum_ws_connections or vector(0)',
    "outbox_publish_rate": 'sum(rate(forum_outbox_published_total[1m])) or vector(0)',
}
start, end = $BENCH_START_EPOCH - 120, $BENCH_END_EPOCH + 60
out = {"start": start, "end": end, "step": "15s", "results": {}}
for name, q in queries.items():
    url = (f"http://localhost:$PROM_LOCAL_PORT/api/v1/query_range?"
           + urllib.parse.urlencode({"query": q, "start": start, "end": end, "step": "15s"}))
    try:
        with urllib.request.urlopen(url, timeout=30) as r:
            out["results"][name] = {"query": q, "response": json.load(r)}
    except Exception as e:  # snapshot best-effort per query; a rename shows up as an error entry
        out["results"][name] = {"query": q, "error": str(e)}
json.dump(out, open(sys.argv[1], "w"))
errs = [n for n, v in out["results"].items() if "error" in v]
print(f"snapshots: {len(out['results']) - len(errs)}/{len(out['results'])} queries ok" + (f" (failed: {errs})" if errs else ""))
EOF
kill "$PF_PID" 2>/dev/null || true
wait "$PF_PID" 2>/dev/null || true

# --- 7. limiter restore happens via the EXIT trap; compute the cross-repeat statistics -----------
step "Statistics across $REPEATS repeats"
python3 - "$ARCHIVE" <<'EOF'
import glob, json, statistics, sys
archive = sys.argv[1]
rows = []
for f in sorted(glob.glob(f"{archive}/run-*/summary-*.json")):
    d = json.load(open(f))
    dur = d["metrics"].get("http_req_duration{scenario:http}") or d["metrics"]["http_req_duration"]
    failed = d["metrics"].get("http_req_failed{scenario:http}") or d["metrics"]["http_req_failed"]
    rows.append({"file": f.split("/")[-2], "rps": d["metrics"]["http_reqs"]["values"]["rate"],
                 "p95_ms": dur["values"]["p(95)"], "p99_ms": dur["values"]["p(99)"],
                 "failed_pct": failed["values"]["rate"] * 100,
                 "reqs": d["metrics"]["http_reqs"]["values"]["count"]})
if not rows:
    sys.exit("no summaries found in the archive")
def agg(key):
    vals = [r[key] for r in rows]
    return {"mean": statistics.mean(vals), "stddev": statistics.stdev(vals) if len(vals) > 1 else 0.0, "values": vals}
stats = {"runs": rows, "rps": agg("rps"), "p95_ms": agg("p95_ms"), "p99_ms": agg("p99_ms"), "failed_pct": agg("failed_pct")}
json.dump(stats, open(f"{archive}/stats.json", "w"), indent=2)
print(f"  runs analyzed: {len(rows)}")
print(f"  req/s : {stats['rps']['mean']:8.1f} ± {stats['rps']['stddev']:.1f}")
print(f"  p95   : {stats['p95_ms']['mean']:8.1f} ± {stats['p95_ms']['stddev']:.1f} ms")
print(f"  p99   : {stats['p99_ms']['mean']:8.1f} ± {stats['p99_ms']['stddev']:.1f} ms")
print(f"  failed: {stats['failed_pct']['mean']:8.2f} %")
EOF

echo
step "Benchmark fairness checklist (thesis method section)"
info "[x] same seed volumes as B ($SEED_COUNTS — deterministic 9b Benchmark profile)"
info "[x] resource limits identical to B's contract (§1: backend 750m/512Mi ×3 HPA)"
info "[x] A and B never run simultaneously (single minikube VM)"
info "[x] warm-up run discarded ($($WARMUP && echo yes || echo 'SKIPPED — note this in the thesis'))"
info "[x] $REPEATS repeats, mean + stddev reported (stats.json)"
info "[x] same k6 host (WSL, outside cluster), same profiles, think time 0.3–0.7 s"
info "[x] git SHA + image digest + date in meta.json"
info "[x] rate limiter raise recorded in meta.json (single-IP artifact, not a production setting)"
info "[x] write-mix growth across repeats is symmetric A/B (same profile, same repeat count);"
info "    meta.json records the exact pre-run row counts — reseed between benchmark DAYS:"
info "    make seed ARGS='--benchmark --cluster'"
info "[ ] Grafana screenshots for the run window ($(date -d "@$BENCH_START_EPOCH" +%H:%M)–$(date -d "@$BENCH_END_EPOCH" +%H:%M)) — export manually"
echo
ok "Archive: ${ARCHIVE#$REPO_ROOT/}"
