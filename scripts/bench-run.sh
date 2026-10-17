#!/usr/bin/env bash
# Phase 9c — the measured-benchmark orchestrator; target-aware since the A/B alignment
# (AB-ALIGNMENT-INSTRUCTIONS-A.md A-03). Produces the thesis numbers for Architecture A (default) or B.
#
#   scripts/bench-run.sh [demo|stress|capacity] [--repeats N] [--skip-warmup] [--target a|b] [--allow-mismatch]
#
# For the A/B comparison itself prefer scripts/ab-round.sh (both targets interleaved, reseed + fresh pods per run,
# generated report). This script stays the single-target tool and shares its checks with it (lib-bench.sh).
#
# Flow: preflight (target deployment ready + monitoring + Benchmark seed present) → record meta.json → neutralize
# the per-IP rate limiter (A: raise RateLimiting__* and ALWAYS restore via trap; B: assert GOMX_AUTH_RATELIMIT=0
# from the bench overlay) → one discarded smoke warm-up (JIT/pools/caches) → N repeats of the profile with 2 min
# cool-downs → Prometheus range snapshots over the whole window → mean±stddev.
#
# Everything lands in thesis/results/<A|B>/<stamp>-<profile>/:
#   meta.json                run metadata: git SHA, image digest, replicas/HPA/resources, limiter values, seed counts
#   warmup/                  the discarded warm-up artifacts (kept for honesty, never analyzed)
#   run-1..N/                summary-*.json + journeys-*.json + samples-*.json + events.txt per repeat
#   prometheus-snapshots.json  query_range results (15 s step) for the target's dashboard queries
#   stats.json               mean ± stddev of req/s and p95 across the N repeats
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env
source "$LIB_DIR/lib-bench.sh"

PROFILE=demo
ALLOW_MISMATCH=false
REPEATS=3
WARMUP=true
TARGET="${TARGET:-a}"
while [[ $# -gt 0 ]]; do
  case "$1" in
    demo|stress|capacity) PROFILE="$1"; shift ;;
    --allow-mismatch) ALLOW_MISMATCH=true; shift ;;
    smoke) die "smoke is the warm-up profile, not a measured one (use demo|stress)" ;;
    --repeats) REPEATS="${2:?--repeats needs a number}"; shift 2 ;;
    --skip-warmup) WARMUP=false; shift ;;
    --target) TARGET="${2:?--target needs a|b}"; shift 2 ;;
    *) die "Unknown argument: $1 (usage: bench-run.sh [demo|stress|capacity] [--repeats N] [--skip-warmup] [--target a|b] [--allow-mismatch])" ;;
  esac
done

case "$TARGET" in
  a) BENCH_NS="$K8S_NAMESPACE";  BENCH_DEPLOY=backend; BENCH_HPA=backend; BENCH_LABEL="app=backend"; ARCH_DIR=A
     DEFAULT_BASE="https://$INGRESS_HOST";      ARCH_NAME="A (React SPA + .NET 10 modular monolith)"; CODE_DIR="$REPO_ROOT" ;;
  b) BENCH_NS="$GOMX_NAMESPACE"; BENCH_DEPLOY=gomx;    BENCH_HPA=gomx;    BENCH_LABEL="app=gomx";    ARCH_DIR=B
     DEFAULT_BASE="https://$GOMX_INGRESS_HOST"; ARCH_NAME="B (Go + Templ/HTMX SSR monolith)";        CODE_DIR="$GOMX_DIR" ;;
  *) die "TARGET must be a|b (got '$TARGET')" ;;
esac
BASE_URL="${BASE_URL:-$DEFAULT_BASE}"
PROM_LOCAL_PORT="${PROM_LOCAL_PORT:-19091}"   # NOT 19090 — that one belongs to dev-tunnels
COOLDOWN_SECONDS="${COOLDOWN_SECONDS:-120}"
bk() { kubectl -n "$BENCH_NS" "$@"; }

# --- 1. preflight -------------------------------------------------------------------------------
step "Preflight [target $TARGET → $BENCH_NS/$BENCH_DEPLOY]"
require_cmd k6 "install: https://grafana.com/docs/k6/latest/set-up/install-k6/"
require_cmd kubectl
require_cmd python3
bk get deployment "$BENCH_DEPLOY" >/dev/null 2>&1 || die "$BENCH_DEPLOY deployment not found in $BENCH_NS — deploy first (A: make mk-deploy; B: scripts/gomx-bench-deploy.sh)."
bk rollout status deployment/"$BENCH_DEPLOY" --timeout=60s >/dev/null || die "$BENCH_DEPLOY not ready."
kubectl -n "$MONITORING_NAMESPACE" get svc monitoring-kube-prometheus-prometheus >/dev/null 2>&1 \
  || die "monitoring stack not found — the measured run needs Prometheus snapshots (make mon-up)."
[[ -d "$CODE_DIR/.git" ]] || die "source checkout for target $TARGET not found at $CODE_DIR."

if [[ "$TARGET" == "a" ]]; then
  # Completed migrate/seed Job pods carry `app: backend` (postgres NetworkPolicy) which ALSO matches the backend
  # Deployment/HPA selector — a lingering terminated pod has no metrics, so the HPA reports "did not receive metrics
  # for targeted pods" and refuses to scale up, silently flat-lining the demo staircase (found live in 9c). The Job
  # manifests now self-clean via ttlSecondsAfterFinished, but a reseed right before the bench can leave one inside its
  # ttl window — sweep any terminated app=backend pods now (never touches Running Deployment pods).
  STALE_JOB_PODS="$(bk get pods -l app=backend --field-selector=status.phase=Succeeded -o name 2>/dev/null; bk get pods -l app=backend --field-selector=status.phase=Failed -o name 2>/dev/null)"
  if [[ -n "$STALE_JOB_PODS" ]]; then
    step "Removing terminated app=backend Job pods (they pollute the HPA metric pool)"
    echo "$STALE_JOB_PODS" | xargs -r bk delete >/dev/null 2>&1 || true
  fi
  # Seed sentinel: the cluster runs ONE database (the --benchmark k8s Job reseeds it in place).
  CLUSTER_DB="$(bk get secret postgres-credentials -o jsonpath='{.data.POSTGRES_DB}' | base64 -d)"
  PSQL=(bk exec postgres-0 -- psql -U "$POSTGRES_USER" -d "$CLUSTER_DB" -tAc)
  USER_COUNT="$("${PSQL[@]}" 'SELECT count(*) FROM forum_identity.users' 2>/dev/null | tr -d '[:space:]')"
  # Scalar subqueries in ONE row: UNION ALL branch order is not guaranteed (parallel Append) — found live.
  SEED_SQL="SELECT (SELECT count(*) FROM forum_identity.users) || ' ' ||
                   (SELECT count(*) FROM forum_content.threads) || ' ' ||
                   (SELECT count(*) FROM forum_content.comments) || ' ' ||
                   (SELECT count(*) FROM forum_engagement.reactions)"
else
  CLUSTER_DB="$(bk get secret gomx-secrets -o jsonpath='{.data.POSTGRES_DB}' | base64 -d)"
  GOMX_PG_USER="$(bk get secret gomx-secrets -o jsonpath='{.data.POSTGRES_USER}' | base64 -d)"
  PSQL=(bk exec postgres-0 -- psql -U "$GOMX_PG_USER" -d "$CLUSTER_DB" -tAc)
  USER_COUNT="$("${PSQL[@]}" 'SELECT count(*) FROM accounts' 2>/dev/null | tr -d '[:space:]')"
  SEED_SQL="SELECT (SELECT count(*) FROM accounts) || ' ' ||
                   (SELECT count(*) FROM posts) || ' ' ||
                   (SELECT count(*) FROM comments) || ' ' ||
                   ((SELECT count(*) FROM post_votes) + (SELECT count(*) FROM comment_likes))"
fi
[[ "$USER_COUNT" =~ ^[0-9]+$ ]] || die "Could not count users in '$CLUSTER_DB' — is Postgres up in $BENCH_NS?"
(( USER_COUNT >= 100 )) || die "Only $USER_COUNT users in '$CLUSTER_DB' — not the Benchmark seed. A: make seed ARGS='--benchmark --cluster'; B: scripts/gomx-bench-deploy.sh --reset-db"
SEED_COUNTS="$("${PSQL[@]}" "$SEED_SQL" | tr -d '\n')"
ok "Benchmark seed present in '$CLUSTER_DB' (users/threads/comments/reactions: $SEED_COUNTS)"

# The common A/B profile (fixed replicas, no HPA, resources, probes, tracing off, pool 30, Postgres image+args).
# The limiter raise is applied below; check it AFTER the raise — here everything else must already hold.
if ! AB_CHECK_OUT="$(ab_check_profile "$TARGET" 2>&1)"; then
  echo "$AB_CHECK_OUT" | grep -E "FAIL" | grep -v "RateLimiting__" >&2 && { $ALLOW_MISMATCH \
    && warn "common-profile mismatch recorded (--allow-mismatch)" \
    || die "live objects deviate from the common A/B profile (FAIL lines above) — fix or pass --allow-mismatch."; } || true
fi

STAMP="$(date +%Y%m%d-%H%M)"
ARCHIVE="$REPO_ROOT/thesis/results/$ARCH_DIR/$STAMP-$PROFILE"
mkdir -p "$ARCHIVE"

# --- 2. meta.json (every thesis number maps to an exact build + environment) ---------------------
step "Recording run metadata"
DEPLOYED_IMAGE="$(bk get deployment "$BENCH_DEPLOY" -o jsonpath='{.spec.template.spec.containers[0].image}')"
IMAGE_DIGEST="$(bk get pods -l "$BENCH_LABEL" --field-selector=status.phase=Running -o jsonpath='{.items[0].status.containerStatuses[0].imageID}')"
GIT_SHA="$(git -C "$CODE_DIR" rev-parse HEAD)"
GIT_DIRTY=false; [[ -n "$(git -C "$CODE_DIR" status --porcelain)" ]] && GIT_DIRTY=true
HARNESS_SHA="$(git -C "$REPO_ROOT" rev-parse HEAD)"
MIX_VERSION="$(sed -n "s/^export const MIX_VERSION = '\([^']*\)';.*/\1/p" "$REPO_ROOT/load/k6/main.js")"
REPLICAS="$(bk get deployment "$BENCH_DEPLOY" -o jsonpath='{.spec.replicas}')"
RESOURCES_JSON="$(bk get deployment "$BENCH_DEPLOY" -o jsonpath='{.spec.template.spec.containers[0].resources}')"
HPA_JSON="$(bk get hpa "$BENCH_HPA" -o jsonpath='{"min":{.spec.minReplicas},"max":{.spec.maxReplicas},"cpu_target":{.spec.metrics[0].resource.target.averageUtilization}}' 2>/dev/null || true)"
HPA_PRESENT=false; [[ -n "$HPA_JSON" ]] && HPA_PRESENT=true
ENV_JSON="$(bk get deployment "$BENCH_DEPLOY" -o json | python3 -c 'import json,sys; c=json.load(sys.stdin)["spec"]["template"]["spec"]["containers"][0]; print(json.dumps({e["name"]: e.get("value") for e in c.get("env", []) if "value" in e}))')"

# Defined for both targets: the meta.json heredoc below expands them under set -u even on the B branch.
ORIG_GLOBAL_OVERRIDE=""; ORIG_AUTH_OVERRIDE=""; CONFIGMAP_GLOBAL=""; CONFIGMAP_AUTH=""
if [[ "$TARGET" == "a" ]]; then
  ORIG_GLOBAL_OVERRIDE="$(bk get deployment backend -o jsonpath='{.spec.template.spec.containers[0].env[?(@.name=="RateLimiting__Global__PermitLimit")].value}')"
  ORIG_AUTH_OVERRIDE="$(bk get deployment backend -o jsonpath='{.spec.template.spec.containers[0].env[?(@.name=="RateLimiting__Auth__PermitLimit")].value}')"
  CONFIGMAP_GLOBAL="$(bk get configmap backend-config -o jsonpath='{.data.RateLimiting__Global__PermitLimit}' 2>/dev/null || echo 100)"
  CONFIGMAP_AUTH="$(bk get configmap backend-config -o jsonpath='{.data.RateLimiting__Auth__PermitLimit}' 2>/dev/null || echo 10)"
fi

python3 - "$ARCHIVE/meta.json" <<EOF
import json, subprocess, sys
def sh(cmd):
    try: return subprocess.run(cmd, shell=True, capture_output=True, text=True, timeout=30).stdout.strip()
    except Exception: return ""
seed = "$SEED_COUNTS".split()
env = json.loads('''$ENV_JSON''')
target = "$TARGET"
meta = {
    "architecture": "$ARCH_NAME",
    "target": target,
    "profile": "$PROFILE",
    "mix_version": "$MIX_VERSION",
    "repeats": $REPEATS,
    "started_utc": sh("date -u +%Y-%m-%dT%H:%M:%SZ"),
    "base_url": "$BASE_URL",
    "git_sha": "$GIT_SHA",
    "git_dirty": "$GIT_DIRTY" == "true",
    "harness_git_sha": "$HARNESS_SHA",
    "deployed_image": "$DEPLOYED_IMAGE",
    "image_digest": "$IMAGE_DIGEST",
    "k6_version": sh("k6 version"),
    "replicas_fixed": int("$REPLICAS" or 0),
    "hpa": json.loads('''$HPA_JSON''') if "$HPA_PRESENT" == "true" else "absent (fixed replicas)",
    "resources": json.loads('''$RESOURCES_JSON''') if '''$RESOURCES_JSON''' else {},
    "seed": {"database": "$CLUSTER_DB",
             "users": int(seed[0]), "threads": int(seed[1]), "comments": int(seed[2]), "reactions": int(seed[3])},
    "node": {"kubectl_nodes": sh("kubectl get nodes -o wide --no-headers"),
             "kubectl_top_node": sh("kubectl top nodes --no-headers"),
             "minikube_vm": sh("docker inspect $MINIKUBE_PROFILE --format '{{.HostConfig.NanoCpus}} nanocpus / {{.HostConfig.Memory}} bytes'"),
             "wsl_kernel": sh("uname -r"),
             "wsl_mem_mb": sh("free -m | awk '/^Mem:/{print \$2}'")},
    "k6_host": "WSL host, outside the cluster, through ingress-nginx (plan §9c design decision)",
}
if target == "a":
    meta["rate_limiter"] = {
        # The raise is required and RECORDED: all k6 traffic egresses ONE host IP, so the per-IP
        # production posture would 429 the benchmark itself (plan §9c / G21). Auth is raised too —
        # setup() logs in 200 users from that same single IP, which no per-IP window models fairly.
        "raised_for_run": {"RateLimiting__Global__PermitLimit": "1000000", "RateLimiting__Auth__PermitLimit": "1000000"},
        "restored_to": {"global_env_override": "$ORIG_GLOBAL_OVERRIDE" or None, "auth_env_override": "$ORIG_AUTH_OVERRIDE" or None,
                        "configmap_global": "$CONFIGMAP_GLOBAL", "configmap_auth": "$CONFIGMAP_AUTH"},
    }
    meta["tracing"] = {"OTEL_TRACES_SAMPLER": env.get("OTEL_TRACES_SAMPLER"), "Observability__TracingEnabled": env.get("Observability__TracingEnabled")}
else:
    meta["rate_limiter"] = {"GOMX_AUTH_RATELIMIT": env.get("GOMX_AUTH_RATELIMIT"), "GOMX_BEACON_RATELIMIT": env.get("GOMX_BEACON_RATELIMIT")}
    meta["tracing"] = {"OTEL_EXPORTER_OTLP_ENDPOINT": env.get("OTEL_EXPORTER_OTLP_ENDPOINT") or "(unset → off)"}
    meta["bench_overlay_env"] = {k: env.get(k) for k in ["GOMX_SEED", "GOMX_COMPRESS", "GOMX_NOTIFY_FRIENDS_ON_POST", "GOMX_SECURE_COOKIES", "GOMX_DB_MAX_CONNS"]}
    # B-09 (+ the 2026-10-17 placement fix) aligned volumes AND shape; what remains is recorded per run.
    meta["seed_deviations"] = [
        "timestamps: B's ages cycle through the feed's range buckets (seedAge(i % 500)); A's are monotonic in the index "
        "(neutralized for J2/J3/J5/J7 by the whole-corpus thread pool, decision D5)",
        "social rows: B 2400 friendships + 80 pending + 2000 DMs; A 0 (A-11 P1; J9 not in mix v1)",
        "soft delete / blocked: A ~1% soft-deleted threads + comments and 20 blocked users; B none (no soft delete, D-8)",
        "tags: A threads carry 0-5 tags; B has no tags",
    ]
json.dump(meta, open(sys.argv[1], "w"), indent=2)
EOF
ok "meta.json written"

# --- 3. neutralize the rate limiter -----------------------------------------------------------------
if [[ "$TARGET" == "a" ]]; then
  restore_limiter() {
    step "Restoring rate limiter"
    if [[ -n "$ORIG_GLOBAL_OVERRIDE" || -n "$ORIG_AUTH_OVERRIDE" ]]; then
      # There were explicit overrides before us — put them back verbatim.
      bk set env deployment/backend \
        "RateLimiting__Global__PermitLimit=${ORIG_GLOBAL_OVERRIDE:-$CONFIGMAP_GLOBAL}" \
        "RateLimiting__Auth__PermitLimit=${ORIG_AUTH_OVERRIDE:-$CONFIGMAP_AUTH}" >/dev/null
    else
      # No overrides existed — remove ours so the configmap values apply again.
      bk set env deployment/backend RateLimiting__Global__PermitLimit- RateLimiting__Auth__PermitLimit- >/dev/null
    fi
    bk rollout status deployment/backend --timeout=180s >/dev/null || warn "rollout after limiter restore did not settle."
    ok "limiter restored (configmap: global=$CONFIGMAP_GLOBAL auth=$CONFIGMAP_AUTH)"
    ab_prom_stop
  }
  step "Raising rate limiter for the measured run"
  bk set env deployment/backend RateLimiting__Global__PermitLimit=1000000 RateLimiting__Auth__PermitLimit=1000000 >/dev/null
  trap restore_limiter EXIT
  bk rollout status deployment/backend --timeout=180s >/dev/null || die "rollout after limiter raise failed."
  LIMITER_ROLLOUT_EPOCH="$(date +%s)"
  ok "PermitLimit=1000000 (global+auth) live"
else
  step "Checking B's limiter (bench overlay)"
  AUTH_LIMIT="$(python3 -c 'import json,sys; print(json.loads(sys.argv[1]).get("GOMX_AUTH_RATELIMIT",""))' "$ENV_JSON")"
  [[ "$AUTH_LIMIT" == "0" ]] || die "GOMX_AUTH_RATELIMIT is '${AUTH_LIMIT:-<unset>}' on deployment gomx — the bench overlay must set 0 (recorded, not a production setting)."
  LIMITER_ROLLOUT_EPOCH="$(date -d "$(bk get pods -l "$BENCH_LABEL" --field-selector=status.phase=Running -o jsonpath='{.items[0].status.startTime}')" +%s 2>/dev/null || date +%s)"
  ok "GOMX_AUTH_RATELIMIT=0 live"
fi

BENCH_START_EPOCH="$(date +%s)"
ab_prom_start   # one port-forward for the per-run window metrics AND the final snapshots
[[ "$TARGET" == "b" ]] && trap ab_prom_stop EXIT   # A's EXIT trap (restore_limiter) stops it too

# --- 4. warm-up (discarded: primes JIT, connection pools, buffer cache) --------------------------
if $WARMUP; then
  step "Warm-up (smoke, discarded)"
  mkdir -p "$ARCHIVE/warmup"
  TARGET="$TARGET" RESULTS_DIR="$ARCHIVE/warmup" bash "$LIB_DIR/run-load-test.sh" smoke "$BASE_URL" || warn "warm-up reported failure — continuing (it is discarded)."
fi

# HPA CPU-initialization guard (found live in 9c): kube-controller-manager ignores the CPU of pods younger than
# ~5 min (--horizontal-pod-autoscaler-cpu-initialization-period) when computing scale-up. Only relevant when an
# HPA exists (fixed-replica Day-0 runs skip it).
if $HPA_PRESENT; then
  HPA_GRACE_REMAINING=$(( ${HPA_CPU_INIT_GRACE:-300} - ($(date +%s) - LIMITER_ROLLOUT_EPOCH) ))
  if (( HPA_GRACE_REMAINING > 0 )); then
    info "waiting ${HPA_GRACE_REMAINING}s for $BENCH_DEPLOY pods to age past the HPA CPU-initialization period"
    sleep "$HPA_GRACE_REMAINING"
  fi
fi

# --- 5. N measured repeats with cool-downs -------------------------------------------------------
for (( i=1; i<=REPEATS; i++ )); do
  step "Measured run $i/$REPEATS ($PROFILE, target $TARGET)"
  RUN_DIR="$ARCHIVE/run-$i"
  mkdir -p "$RUN_DIR"
  # run-load-test.sh already maps stress threshold breaches to exit 0 (informational by design),
  # so a non-zero here is a real crash — tolerated only for stress (partial knee data still counts).
  TARGET="$TARGET" RESULTS_DIR="$RUN_DIR" bash "$LIB_DIR/run-load-test.sh" "$PROFILE" "$BASE_URL" \
    || { [[ "$PROFILE" == "stress" ]] && warn "stress run $i failed (rc≠0) — continuing with partial data." \
         || die "measured run $i failed — archive is incomplete, aborting."; }
  bk get events --sort-by=.lastTimestamp >"$RUN_DIR/events.txt" 2>/dev/null || true
  ab_window_metrics "$TARGET" "$RUN_DIR" || warn "window metrics failed for run $i (resources.json missing)."
  if (( i < REPEATS )); then
    info "cool-down ${COOLDOWN_SECONDS}s (drains scale-down / outbox backlog before the next repeat)"
    sleep "$COOLDOWN_SECONDS"
  fi
done
BENCH_END_EPOCH="$(date +%s)"

# --- 6. Prometheus range snapshots over the whole window ------------------------------------------
step "Prometheus snapshots"
python3 - "$ARCHIVE/prometheus-snapshots.json" <<EOF || warn "Prometheus snapshot failed — dashboards remain the fallback."
import json, sys, urllib.parse, urllib.request
target, ns, pod_re = "$TARGET", "$BENCH_NS", "$BENCH_DEPLOY-.*"
# Same KEYS for both targets so the thesis join is one script; the expressions are per side because the metric
# families differ (A: OTel http_server_request_duration_seconds; B: Go client http_request_duration_seconds).
if target == "a":
    queries = {
        "rps":            'sum(rate(http_server_request_duration_seconds_count{job="backend"}[1m]))',
        "p95_ms":         'histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket{job="backend", http_route!="/api/realtime/ws"}[1m]))) * 1000',
        "p99_ms":         'histogram_quantile(0.99, sum by (le) (rate(http_server_request_duration_seconds_bucket{job="backend", http_route!="/api/realtime/ws"}[1m]))) * 1000',
        "rps_by_route":   'sum by (http_route) (rate(http_server_request_duration_seconds_count{job="backend"}[1m]))',
        "p95_by_route_ms":'histogram_quantile(0.95, sum by (le, http_route) (rate(http_server_request_duration_seconds_bucket{job="backend", http_route!="/api/realtime/ws"}[5m]))) * 1000',
        "errors_5xx":     'sum(rate(http_server_request_duration_seconds_count{job="backend", http_response_status_code=~"5.."}[1m])) or vector(0)',
        "ws_connections": 'forum_ws_connections or vector(0)',
        "outbox_publish_rate": 'sum(rate(forum_outbox_published_total[1m])) or vector(0)',
        "rabbit_poison":  'sum(rabbitmq_queue_messages_ready{queue=~".*poison"}) or vector(0)',
        "pg_connections": 'sum(pg_stat_activity_count)',
    }
else:
    queries = {
        "rps":            'sum(rate(http_requests_total{namespace="%s"}[1m]))' % ns,
        "p95_ms":         'histogram_quantile(0.95, sum by (le) (rate(http_request_duration_seconds_bucket{namespace="%s", route!="/ws"}[1m]))) * 1000' % ns,
        "p99_ms":         'histogram_quantile(0.99, sum by (le) (rate(http_request_duration_seconds_bucket{namespace="%s", route!="/ws"}[1m]))) * 1000' % ns,
        "rps_by_route":   'sum by (route) (rate(http_requests_total{namespace="%s"}[1m]))' % ns,
        "p95_by_route_ms":'histogram_quantile(0.95, sum by (le, route) (rate(http_request_duration_seconds_bucket{namespace="%s", route!="/ws"}[5m]))) * 1000' % ns,
        "errors_5xx":     'sum(rate(http_requests_total{namespace="%s", code=~"5.."}[1m])) or vector(0)' % ns,
        "ws_connections": 'sum(forum_ws_connected_clients{namespace="%s"}) or vector(0)' % ns,
        "writes_rejected": 'sum(rate(forum_writes_rejected_total{namespace="%s"}[1m])) or vector(0)' % ns,
    }
# Namespace-level resource series (minikube cAdvisor exports pod-slice series only — container="").
queries.update({
    "hpa_replicas":   'kube_horizontalpodautoscaler_status_current_replicas{namespace="%s", horizontalpodautoscaler="$BENCH_HPA"} or vector(0)' % ns,
    "hpa_desired":    'kube_horizontalpodautoscaler_status_desired_replicas{namespace="%s", horizontalpodautoscaler="$BENCH_HPA"} or vector(0)' % ns,
    "app_cpu_cores":  'sum by (pod) (rate(container_cpu_usage_seconds_total{namespace="%s", pod=~"%s", container=""}[2m]))' % (ns, pod_re),
    "app_mem_bytes":  'sum by (pod) (container_memory_working_set_bytes{namespace="%s", pod=~"%s", container=""})' % (ns, pod_re),
    "ns_cpu_cores":   'sum by (pod) (rate(container_cpu_usage_seconds_total{namespace="%s", container=""}[2m]))' % ns,
    "ns_mem_bytes":   'sum by (pod) (container_memory_working_set_bytes{namespace="%s", container=""})' % ns,
    "running_pods":   'count(kube_pod_status_phase{namespace="%s", phase="Running"} == 1)' % ns,
})
start, end = $BENCH_START_EPOCH - 120, $BENCH_END_EPOCH + 60
out = {"target": target, "namespace": ns, "start": start, "end": end, "step": "15s", "results": {}}
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
ab_prom_stop

# --- 7. cross-repeat statistics (A's limiter restore happens via the EXIT trap) -------------------
step "Statistics across $REPEATS repeats"
python3 - "$ARCHIVE" <<'EOF'
import glob, json, statistics, sys
archive = sys.argv[1]
rows = []
for f in sorted(glob.glob(f"{archive}/run-*/summary-*.json")):
    d = json.load(open(f))
    dur = d["metrics"].get("http_req_duration{scenario:http}") or d["metrics"]["http_req_duration"]
    failed = d["metrics"].get("http_req_failed{scenario:http}") or d["metrics"]["http_req_failed"]
    row = {"file": f.split("/")[-2], "rps": d["metrics"]["http_reqs"]["values"]["rate"],
           "p95_ms": dur["values"]["p(95)"], "p99_ms": dur["values"]["p(99)"],
           "failed_pct": failed["values"]["rate"] * 100,
           "reqs": d["metrics"]["http_reqs"]["values"]["count"]}
    jm = d["metrics"].get("forum_journeys")
    if jm:
        row["journeys_per_s"] = jm["values"]["rate"]
    rows.append(row)
if not rows:
    sys.exit("no summaries found in the archive")
def agg(key):
    vals = [r[key] for r in rows if key in r]
    if not vals:
        return None
    return {"mean": statistics.mean(vals), "stddev": statistics.stdev(vals) if len(vals) > 1 else 0.0, "values": vals}
stats = {"runs": rows, "rps": agg("rps"), "journeys_per_s": agg("journeys_per_s"),
         "p95_ms": agg("p95_ms"), "p99_ms": agg("p99_ms"), "failed_pct": agg("failed_pct")}
json.dump(stats, open(f"{archive}/stats.json", "w"), indent=2)
print(f"  runs analyzed: {len(rows)}")
print(f"  req/s : {stats['rps']['mean']:8.1f} ± {stats['rps']['stddev']:.1f}")
if stats["journeys_per_s"]:
    print(f"  jrn/s : {stats['journeys_per_s']['mean']:8.2f} ± {stats['journeys_per_s']['stddev']:.2f}   (journeys/sec — the comparable unit)")
print(f"  p95   : {stats['p95_ms']['mean']:8.1f} ± {stats['p95_ms']['stddev']:.1f} ms")
print(f"  p99   : {stats['p99_ms']['mean']:8.1f} ± {stats['p99_ms']['stddev']:.1f} ms")
print(f"  failed: {stats['failed_pct']['mean']:8.2f} %")
EOF

echo
step "Benchmark fairness checklist (thesis method section)"
info "[x] target $TARGET: seed volumes $SEED_COUNTS (B shape deviations recorded in meta.json until B-09)"
info "[x] common resource profile recorded from the live deployment (meta.json resources/replicas/hpa)"
info "[x] A and B never run simultaneously (single minikube VM) — scale the idle side to 0"
info "[x] warm-up run discarded ($($WARMUP && echo yes || echo 'SKIPPED — note this in the thesis'))"
info "[x] $REPEATS repeats, mean + stddev reported (stats.json)"
info "[x] same k6 host (WSL, outside cluster), same harness/mix ($MIX_VERSION), think time 0.3–0.7 s"
info "[x] git SHA + image digest + date in meta.json"
info "[x] rate limiter neutralization recorded in meta.json (single-IP artifact, not a production setting)"
info "[x] write-mix growth across repeats is symmetric A/B (same profile, same repeat count);"
info "    meta.json records the exact pre-run row counts — reseed between benchmark DAYS."
info "[ ] Grafana screenshots for the run window ($(date -d "@$BENCH_START_EPOCH" +%H:%M)–$(date -d "@$BENCH_END_EPOCH" +%H:%M)) — export manually"
echo
ok "Archive: ${ARCHIVE#$REPO_ROOT/}"
