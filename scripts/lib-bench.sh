#!/usr/bin/env bash
# Shared A/B benchmark helpers (sourced by ab-round.sh and bench-run.sh AFTER lib.sh + load_env).
#
# One place encodes the COMMON BENCHMARK PROFILE both architectures must run under (the fairness invariants of
# docs/architecture/AB-ALIGNMENT-INSTRUCTIONS-{A,B}.md §1, F3/F5/F8):
#   app      : FIXED replicas (AB_REPLICAS, default 2), no HPA, requests 250m/256Mi, limits 1000m/512Mi,
#              liveness/readiness timeoutSeconds 5, tracing OFF, rate limiters neutralized (recorded)
#   postgres : postgres:18 (Debian) with shared_buffers=256MB effective_cache_size=512MB max_connections=100,
#              requests 250m/512Mi, limits 1000m/1Gi; app pool 30 per replica
#   B only   : GOMX_SEED=bench, GOMX_COMPRESS=0, GOMX_NOTIFY_FRIENDS_ON_POST=0, GOMX_DB_MAX_CONNS=30
# ab_apply_profile enforces the parts that are live-configurable on A (B's come from its kustomize overlay);
# ab_check_profile VERIFIES all of it from the live objects and refuses to measure a drifted cluster.

: "${AB_REPLICAS:=2}"
: "${PROM_LOCAL_PORT:=19091}"   # NOT 19090 — that one belongs to dev-tunnels

# Round VARIANTS (harness h4, 2026-10-18) — every one is recorded in meta/round.json and verified live:
#   AB_HPA=true          identical HPA 1–3 @ 70 % CPU on both apps (A: k8s/backend/hpa.yaml; B: the same spec
#                        rendered for deployment gomx) instead of fixed replicas; runs START at 1 replica (A-15)
#   AB_GC_VARIANT=true   memory-policy sensitivity (D8): A workstation GC (DOTNET_gcServer=0),
#                        B GOMEMLIMIT=384MiB (75 % of the 512Mi limit, Go's documented practice)
#   AB_RELAY_POLL_MS=N   A's outbox relay poll interval (Messaging__PollIntervalMilliseconds; default 5000 —
#                        the floor of A's WS receipt latency, see the J8 probes)
#   AB_AUTH_READS=true   read journeys carry the session (D3 logged-in variant; forwarded to k6 as AUTH_READS)
: "${AB_HPA:=false}"
: "${AB_GC_VARIANT:=false}"
: "${AB_RELAY_POLL_MS:=}"
: "${AB_AUTH_READS:=false}"
AB_HPA_MIN=1; AB_HPA_MAX=3; AB_HPA_CPU=70
AB_B_GOMEMLIMIT="384MiB"
ab_start_replicas() { if [[ "$AB_HPA" == true ]]; then echo "$AB_HPA_MIN"; else echo "$AB_REPLICAS"; fi; }

ab_ns()     { [[ "$1" == a ]] && echo "$K8S_NAMESPACE" || echo "$GOMX_NAMESPACE"; }
ab_deploy() { [[ "$1" == a ]] && echo backend || echo gomx; }
ab_label()  { [[ "$1" == a ]] && echo app=backend || echo app=gomx; }
ab_dir()    { [[ "$1" == a ]] && echo A || echo B; }
ab_base()   { [[ "$1" == a ]] && echo "https://$INGRESS_HOST" || echo "https://$GOMX_INGRESS_HOST"; }
ab_code()   { [[ "$1" == a ]] && echo "$REPO_ROOT" || echo "$GOMX_DIR"; }
ab_name()   { [[ "$1" == a ]] && echo "A (React SPA + .NET 10 modular monolith)" || echo "B (Go + Templ/HTMX SSR monolith)"; }
ab_kubectl() { local t="$1"; shift; kubectl -n "$(ab_ns "$t")" "$@"; }

# Scales the target's app Deployment and waits: Ready pods for n>0, no pods left for n=0.
ab_scale() {
  local t="$1" n="$2" ns deploy label
  ns="$(ab_ns "$t")"; deploy="$(ab_deploy "$t")"; label="$(ab_label "$t")"
  kubectl -n "$ns" scale "deployment/$deploy" --replicas="$n" >/dev/null
  if (( n > 0 )); then
    kubectl -n "$ns" rollout status "deployment/$deploy" --timeout=600s >/dev/null
  else
    kubectl -n "$ns" wait --for=delete pod -l "$label" --timeout=180s >/dev/null 2>&1 || true
  fi
}

# Terminated migrate/seed Job pods carry app=backend on A (postgres NetworkPolicy) — they would match the
# Deployment selector and pollute every pod-level query; sweep them (never touches Running pods).
ab_sweep_job_pods() {
  local stale
  stale="$(kc get pods -l app=backend --field-selector=status.phase=Succeeded -o name 2>/dev/null; kc get pods -l app=backend --field-selector=status.phase=Failed -o name 2>/dev/null)"
  [[ -z "$stale" ]] || echo "$stale" | xargs -r kubectl -n "$K8S_NAMESPACE" delete >/dev/null 2>&1 || true
}

# --- the common profile -----------------------------------------------------------------------------------------

# A's limiter override as found BEFORE we touched it ("" = no override) — restored verbatim by ab_restore_limiter_a.
AB_ORIG_GLOBAL=""; AB_ORIG_AUTH=""; AB_LIMITER_RAISED=false
# The HPA spec both sides share under AB_HPA (A's manifest is the source; B gets the identical spec rendered for
# its deployment/namespace/label — min/max/target/behavior byte-for-byte the same).
ab_hpa_manifest() {
  local t="$1"
  if [[ "$t" == a ]]; then
    cat "$REPO_ROOT/k8s/backend/hpa.yaml"
  else
    sed -e "s/forum-dotnet/$GOMX_NAMESPACE/g" -e 's/backend/gomx/g' "$REPO_ROOT/k8s/backend/hpa.yaml"
  fi
}

ab_apply_profile() {
  local t="$1" ns deploy env_now
  ns="$(ab_ns "$t")"; deploy="$(ab_deploy "$t")"
  if [[ "$AB_HPA" == true ]]; then
    ab_hpa_manifest "$t" | kubectl -n "$ns" apply -f - >/dev/null
  else
    kubectl -n "$ns" delete hpa "$deploy" --ignore-not-found >/dev/null
  fi
  if [[ "$t" == a ]]; then
    ab_sweep_job_pods
    env_now="$(kc get deployment backend -o jsonpath='{range .spec.template.spec.containers[0].env[*]}{.name}={.value}{"\n"}{end}')"
    if ! $AB_LIMITER_RAISED; then
      AB_ORIG_GLOBAL="$(sed -n 's/^RateLimiting__Global__PermitLimit=//p' <<<"$env_now")"
      AB_ORIG_AUTH="$(sed -n 's/^RateLimiting__Auth__PermitLimit=//p' <<<"$env_now")"
    fi
    # One `set env` = at most one rollout; identical values are a no-op (no rollout at all). Variant knobs are set
    # or REMOVED (trailing '-') in the same call so a round never inherits a previous round's variant.
    local gc_a relay_a
    if [[ "$AB_GC_VARIANT" == true ]]; then gc_a="DOTNET_gcServer=0"; else gc_a="DOTNET_gcServer-"; fi
    if [[ -n "$AB_RELAY_POLL_MS" ]]; then relay_a="Messaging__PollIntervalMilliseconds=$AB_RELAY_POLL_MS"; else relay_a="Messaging__PollIntervalMilliseconds-"; fi
    kc set env deployment/backend \
      Observability__TracingEnabled=false \
      RateLimiting__Global__PermitLimit=1000000 \
      RateLimiting__Auth__PermitLimit=1000000 \
      "$gc_a" "$relay_a" >/dev/null
    AB_LIMITER_RAISED=true
  else
    local gc_b
    if [[ "$AB_GC_VARIANT" == true ]]; then gc_b="GOMEMLIMIT=$AB_B_GOMEMLIMIT"; else gc_b="GOMEMLIMIT-"; fi
    ab_kubectl b set env deployment/gomx "$gc_b" >/dev/null
  fi
  ab_scale "$t" "$(ab_start_replicas)"
}

# Puts A's limiter back the way ab_apply_profile found it (tracing stays OFF and the HPA stays deleted — the
# cluster remains in benchmark shape; `make mk-deploy` restores the production posture).
ab_restore_limiter_a() {
  $AB_LIMITER_RAISED || return 0
  if [[ -n "$AB_ORIG_GLOBAL" || -n "$AB_ORIG_AUTH" ]]; then
    local cm_global cm_auth
    cm_global="$(kc get configmap backend-config -o jsonpath='{.data.RateLimiting__Global__PermitLimit}' 2>/dev/null || echo 100)"
    cm_auth="$(kc get configmap backend-config -o jsonpath='{.data.RateLimiting__Auth__PermitLimit}' 2>/dev/null || echo 10)"
    kc set env deployment/backend "RateLimiting__Global__PermitLimit=${AB_ORIG_GLOBAL:-$cm_global}" \
      "RateLimiting__Auth__PermitLimit=${AB_ORIG_AUTH:-$cm_auth}" >/dev/null
  else
    kc set env deployment/backend RateLimiting__Global__PermitLimit- RateLimiting__Auth__PermitLimit- >/dev/null
  fi
  AB_LIMITER_RAISED=false
}

# Verifies the live objects against the common profile. Prints one line per check; returns 1 on any FAIL.
ab_check_profile() {
  local t="$1" ns
  ns="$(ab_ns "$t")"
  python3 - "$t" "$AB_REPLICAS" \
    "$(kubectl -n "$ns" get deployment "$(ab_deploy "$t")" -o json)" \
    "$(kubectl -n "$ns" get statefulset postgres -o json)" \
    "$(kubectl -n "$ns" get hpa -o json)" \
    "$( [[ "$t" == a ]] && kc get secret postgres-credentials -o jsonpath='{.data.CONNECTION_STRING}' | base64 -d || echo '')" \
    "$AB_HPA" "$AB_HPA_MIN" "$AB_HPA_MAX" "$AB_HPA_CPU" "$AB_GC_VARIANT" "$AB_RELAY_POLL_MS" "$AB_B_GOMEMLIMIT" <<'PY'
import json, sys
t, replicas, dep, sts, hpa, conn = sys.argv[1], int(sys.argv[2]), json.loads(sys.argv[3]), json.loads(sys.argv[4]), json.loads(sys.argv[5]), sys.argv[6]
hpa_on, hpa_min, hpa_max, hpa_cpu = sys.argv[7] == "true", int(sys.argv[8]), int(sys.argv[9]), int(sys.argv[10])
gc_variant, relay_ms, b_gomemlimit = sys.argv[11] == "true", sys.argv[12], sys.argv[13]
fails = []
def check(ok, label):
    print(("  ok    " if ok else "  FAIL  ") + label)
    if not ok:
        fails.append(label)
def cpu(v):
    v = str(v)
    return int(v[:-1]) if v.endswith("m") else int(float(v) * 1000)
def mem(v):
    v = str(v)
    for suffix, mult in (("Gi", 1024), ("Mi", 1)):
        if v.endswith(suffix):
            return int(float(v[:-len(suffix)]) * mult)
    return int(v) // (1024 * 1024)
c = dep["spec"]["template"]["spec"]["containers"][0]
env = {e["name"]: e.get("value") for e in c.get("env", [])}
r = c.get("resources", {})
if hpa_on:
    items = hpa.get("items", [])
    check(len(items) == 1, f"exactly one HorizontalPodAutoscaler in the namespace (found {len(items)})")
    if items:
        spec = items[0]["spec"]
        cpu_target = next((m["resource"]["target"].get("averageUtilization") for m in spec.get("metrics", [])
                           if m.get("type") == "Resource" and m["resource"]["name"] == "cpu"), None)
        check(spec.get("minReplicas") == hpa_min and spec.get("maxReplicas") == hpa_max and cpu_target == hpa_cpu,
              f"HPA {spec.get('minReplicas')}–{spec.get('maxReplicas')} @ cpu {cpu_target} % (want {hpa_min}–{hpa_max} @ {hpa_cpu} %, identical on both)")
        check(spec.get("behavior") is not None, "HPA carries the explicit scale-up/down behavior block (same on both)")
    check(1 <= (dep["spec"].get("replicas") or 0) <= hpa_max, f"app replicas = {dep['spec'].get('replicas')} (HPA-owned, within 1–{hpa_max})")
else:
    check(dep["spec"].get("replicas") == replicas, f"app replicas = {dep['spec'].get('replicas')} (want {replicas}, fixed)")
    check(len(hpa.get("items", [])) == 0, "no HorizontalPodAutoscaler in the namespace")
check(cpu(r.get("requests", {}).get("cpu", "0")) == 250 and mem(r.get("requests", {}).get("memory", "0")) == 256,
      f"app requests {r.get('requests')} (want cpu 250m, memory 256Mi)")
check(cpu(r.get("limits", {}).get("cpu", "0")) == 1000 and mem(r.get("limits", {}).get("memory", "0")) == 512,
      f"app limits {r.get('limits')} (want cpu 1, memory 512Mi)")
for probe in ("livenessProbe", "readinessProbe"):
    check(c.get(probe, {}).get("timeoutSeconds") == 5, f"app {probe}.timeoutSeconds = {c.get(probe, {}).get('timeoutSeconds')} (want 5)")
if t == "a":
    check(env.get("Observability__TracingEnabled") == "false", f"tracing off (Observability__TracingEnabled={env.get('Observability__TracingEnabled')})")
    for k in ("RateLimiting__Global__PermitLimit", "RateLimiting__Auth__PermitLimit"):
        check(int(env.get(k) or 0) >= 1000000, f"{k} = {env.get(k)} (want >= 1000000, recorded)")
    check("Maximum Pool Size=30" in conn, "Npgsql pool: 'Maximum Pool Size=30' in CONNECTION_STRING")
    if gc_variant:
        check(env.get("DOTNET_gcServer") == "0", f"GC variant: DOTNET_gcServer = {env.get('DOTNET_gcServer')} (want 0 = workstation GC)")
    else:
        check("DOTNET_gcServer" not in env, f"default GC policy (DOTNET_gcServer unset, found {env.get('DOTNET_gcServer')!r})")
    if relay_ms:
        check(env.get("Messaging__PollIntervalMilliseconds") == relay_ms, f"outbox relay poll = {env.get('Messaging__PollIntervalMilliseconds')} ms (want {relay_ms}, recorded)")
    else:
        check("Messaging__PollIntervalMilliseconds" not in env, f"outbox relay poll = appsettings default 5000 ms (override {env.get('Messaging__PollIntervalMilliseconds')!r} absent)")
else:
    if gc_variant:
        check(env.get("GOMEMLIMIT") == b_gomemlimit, f"GC variant: GOMEMLIMIT = {env.get('GOMEMLIMIT')} (want {b_gomemlimit})")
    else:
        check("GOMEMLIMIT" not in env, f"default GC policy (GOMEMLIMIT unset, found {env.get('GOMEMLIMIT')!r})")
    want = {"GOMX_SEED": "bench", "GOMX_AUTH_RATELIMIT": "0", "GOMX_COMPRESS": "0",
            "GOMX_NOTIFY_FRIENDS_ON_POST": "0", "GOMX_DB_MAX_CONNS": "30"}
    for k, v in want.items():
        check(env.get(k) == v, f"{k} = {env.get(k)} (want {v})")
    check(not (env.get("OTEL_EXPORTER_OTLP_ENDPOINT") or env.get("OTEL_EXPORTER_OTLP_TRACES_ENDPOINT")),
          f"tracing off (OTEL_EXPORTER_OTLP_ENDPOINT={env.get('OTEL_EXPORTER_OTLP_ENDPOINT')!r})")
pg = sts["spec"]["template"]["spec"]["containers"][0]
pr = pg.get("resources", {})
check(pg["image"].split("/")[-1] == "postgres:18", f"postgres image = {pg['image']} (want postgres:18, Debian — same on both)")
args = " ".join(pg.get("args", []))
for a in ("shared_buffers=256MB", "effective_cache_size=512MB", "max_connections=100"):
    check(a in args, f"postgres arg {a}")
check(cpu(pr.get("requests", {}).get("cpu", "0")) == 250 and mem(pr.get("requests", {}).get("memory", "0")) == 512,
      f"postgres requests {pr.get('requests')} (want 250m/512Mi)")
check(cpu(pr.get("limits", {}).get("cpu", "0")) == 1000 and mem(pr.get("limits", {}).get("memory", "0")) == 1024,
      f"postgres limits {pr.get('limits')} (want 1/1Gi)")
sys.exit(1 if fails else 0)
PY
}

# --- dataset -------------------------------------------------------------------------------------------------------

# "users threads comments reactions" of the target's live database (scalar subqueries — UNION ALL branch order is
# not guaranteed under a parallel Append; found live in 9c).
ab_seed_counts() {
  local t="$1"
  if [[ "$t" == a ]]; then
    local db; db="$(kc get secret postgres-credentials -o jsonpath='{.data.POSTGRES_DB}' | base64 -d)"
    kc exec postgres-0 -- psql -U "$POSTGRES_USER" -d "$db" -tAc \
      "SELECT (SELECT count(*) FROM forum_identity.users) || ' ' || (SELECT count(*) FROM forum_content.threads) || ' ' ||
              (SELECT count(*) FROM forum_content.comments) || ' ' || (SELECT count(*) FROM forum_engagement.reactions)" | tr -d '\n'
  else
    local db user
    db="$(ab_kubectl b get secret gomx-secrets -o jsonpath='{.data.POSTGRES_DB}' | base64 -d)"
    user="$(ab_kubectl b get secret gomx-secrets -o jsonpath='{.data.POSTGRES_USER}' | base64 -d)"
    ab_kubectl b exec postgres-0 -- psql -U "$user" -d "$db" -tAc \
      "SELECT (SELECT count(*) FROM accounts) || ' ' || (SELECT count(*) FROM posts) || ' ' ||
              (SELECT count(*) FROM comments) || ' ' || ((SELECT count(*) FROM post_votes) + (SELECT count(*) FROM comment_likes))" | tr -d '\n'
  fi
}

AB_EXPECTED_SEED="800 1600 9000 15000"

# Planner statistics right after the bulk insert, on BOTH sides (harness h4): neither seeder analyzes, so without
# this the first minutes of a run plan against whatever autoanalyze had reached — run-to-run plan noise that was
# only symmetric by luck. ANALYZE (not VACUUM) is cheap (~1 s on the 24 MB seed) and identical on both sides.
ab_analyze() {
  local t="$1"
  if [[ "$t" == a ]]; then
    local db; db="$(kc get secret postgres-credentials -o jsonpath='{.data.POSTGRES_DB}' | base64 -d)"
    kc exec postgres-0 -- psql -U "$POSTGRES_USER" -d "$db" -qc "ANALYZE" >/dev/null
  else
    local db user
    db="$(ab_kubectl b get secret gomx-secrets -o jsonpath='{.data.POSTGRES_DB}' | base64 -d)"
    user="$(ab_kubectl b get secret gomx-secrets -o jsonpath='{.data.POSTGRES_USER}' | base64 -d)"
    ab_kubectl b exec postgres-0 -- psql -U "$user" -d "$db" -qc "ANALYZE" >/dev/null
  fi
}

# Resets the target to the pristine Benchmark seed and leaves AB_REPLICAS fresh, Ready app pods behind — so every
# measured run starts from the identical dataset AND from newly started processes on BOTH sides (B's reseed needs a
# pod restart anyway; A's pods are restarted too so neither side carries warm state from the previous run).
ab_reseed() {
  local t="$1"
  if [[ "$t" == a ]]; then
    local image
    image="$(kc get deployment backend -o jsonpath='{.spec.template.spec.containers[0].image}')"
    kc delete job db-seed-benchmark --ignore-not-found >/dev/null 2>&1 || true
    sed "s|image: $IMAGE_NAME:local|image: $image|" "$REPO_ROOT/k8s/backend/seed-job-benchmark.yaml" | kc apply -f - >/dev/null
    kc wait --for=condition=complete job/db-seed-benchmark --timeout=600s >/dev/null \
      || { kc logs job/db-seed-benchmark --tail=30 >&2 || true; die "A benchmark seed Job failed."; }
    kc delete job db-seed-benchmark --ignore-not-found >/dev/null 2>&1 || true
    ab_sweep_job_pods
    ab_analyze a
    kc rollout restart deployment/backend >/dev/null
    ab_scale a "$(ab_start_replicas)"
  else
    local db user
    db="$(ab_kubectl b get secret gomx-secrets -o jsonpath='{.data.POSTGRES_DB}' | base64 -d)"
    user="$(ab_kubectl b get secret gomx-secrets -o jsonpath='{.data.POSTGRES_USER}' | base64 -d)"
    ab_scale b 0
    ab_kubectl b exec postgres-0 -- psql -U "$user" -d postgres -v ON_ERROR_STOP=1 \
      -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname='$db' AND pid <> pg_backend_pid();" \
      -c "DROP DATABASE IF EXISTS $db;" -c "CREATE DATABASE $db OWNER $user;" >/dev/null
    # One pod seeds (the advisory lock would also serialize two — one first keeps the logs unambiguous).
    ab_scale b 1
    local seeded=false
    for _ in $(seq 1 120); do
      if ab_kubectl b logs deployment/gomx --tail=200 2>/dev/null | grep -q 'benchmark seed complete'; then seeded=true; break; fi
      sleep 5
    done
    $seeded || die "B: no 'benchmark seed complete' after the DB reset (kubectl -n $GOMX_NAMESPACE logs deploy/gomx)."
    # The pod that seeded carries the seeder's heap/GC history; replace it so BOTH replicas are processes that
    # never did anything but serve (A's pods are likewise restarted after its seed Job) — "fresh pods" means the
    # same thing on both sides.
    ab_scale b 0
    ab_analyze b
    ab_scale b "$(ab_start_replicas)"
  fi
  local counts; counts="$(ab_seed_counts "$t")"
  [[ "$counts" == "$AB_EXPECTED_SEED" ]] || die "$(ab_dir "$t") seed counts after reset are '$counts' (want '$AB_EXPECTED_SEED')."
}

# --- metadata ------------------------------------------------------------------------------------------------------

# Writes the target's environment record (image + digest, git state of its checkout, live config, seed, node).
ab_meta() {
  local t="$1" out="$2" ns deploy label
  ns="$(ab_ns "$t")"; deploy="$(ab_deploy "$t")"; label="$(ab_label "$t")"
  python3 - "$out" "$t" "$(ab_name "$t")" "$(ab_code "$t")" "$REPO_ROOT" \
    "$(kubectl -n "$ns" get deployment "$deploy" -o json)" \
    "$(kubectl -n "$ns" get pods -l "$label" -o json)" \
    "$(kubectl -n "$ns" get statefulset postgres -o json)" \
    "$(ab_seed_counts "$t" 2>/dev/null || echo '')" \
    "$AB_HPA" "$AB_GC_VARIANT" "$AB_RELAY_POLL_MS" "$AB_AUTH_READS" \
    "$(kubectl -n "$ns" get hpa -o json 2>/dev/null || echo '{}')" <<'PY'
import json, subprocess, sys
out, t, name, code, harness, dep, pods, sts, seed = sys.argv[1:10]
hpa_on, gc_variant, relay_ms, auth_reads, hpa_json = sys.argv[10:15]
dep, pods, sts = json.loads(dep), json.loads(pods), json.loads(sts)
def sh(cmd):
    try:
        return subprocess.run(cmd, shell=True, capture_output=True, text=True, timeout=30).stdout.strip()
    except Exception:
        return ""
c = dep["spec"]["template"]["spec"]["containers"][0]
running = [p for p in pods.get("items", []) if p.get("status", {}).get("phase") == "Running"]
pg = sts["spec"]["template"]["spec"]["containers"][0]
seed = seed.split()
meta = {
    "architecture": name, "target": t,
    "git_sha": sh(f"git -C {code} rev-parse HEAD"), "git_branch": sh(f"git -C {code} branch --show-current"),
    "git_dirty": bool(sh(f"git -C {code} status --porcelain")),
    "harness_git_sha": sh(f"git -C {harness} rev-parse HEAD"), "harness_dirty": bool(sh(f"git -C {harness} status --porcelain load scripts")),
    "deployed_image": c["image"],
    "image_digest": running[0]["status"]["containerStatuses"][0].get("imageID") if running else None,
    "replicas": dep["spec"].get("replicas"),
    "resources": c.get("resources", {}),
    "probes": {k: c.get(k) for k in ("livenessProbe", "readinessProbe", "startupProbe") if c.get(k)},
    "env": {e["name"]: e.get("value") for e in c.get("env", []) if "value" in e},
    "postgres": {"image": pg["image"], "args": pg.get("args", []), "resources": pg.get("resources", {})},
    "seed_counts": dict(zip(["users", "threads", "comments", "reactions"], map(int, seed))) if len(seed) == 4 else None,
    "k6_version": sh("k6 version"),
    "variants": {"hpa": hpa_on == "true", "gc_variant": gc_variant == "true",
                 "relay_poll_ms": int(relay_ms) if relay_ms else None, "auth_reads": auth_reads == "true",
                 "hpa_spec": [h["spec"] for h in (json.loads(hpa_json).get("items") or [])]},
    "node": {"kubectl_nodes": sh("kubectl get nodes -o wide --no-headers"),
             "allocatable": sh("kubectl get nodes -o jsonpath='{.items[0].status.allocatable}'"),
             "wsl_kernel": sh("uname -r"), "wsl_mem_mb": sh("free -m | awk '/^Mem:/{print $2}'"),
             "host_cpus": sh("nproc")},
}
if t == "b":
    meta["seed_deviations_vs_A"] = [
        "timestamps: B's bench ages cycle through the feed's range buckets (seedAge(i % 500)); A's are monotonic in "
        "the index (newest = highest index). Neutralized for J2/J3/J5/J7 by the whole-corpus thread pool (decision D5).",
        "social rows: B seeds 2400 friendships + 80 pending + 2000 DMs; A's Benchmark seeds 0 (A-11 P1; J9 not in mix v1).",
        "soft delete / blocked: A has ~1% soft-deleted threads + comments and 20 blocked users; B has no soft delete (D-8).",
        "tags: A threads carry 0-5 tags; B has no tags (feature difference, not measured).",
        "pinned: both 16 (every 100th post) but placed by each seed's own category draw.",
    ]
json.dump(meta, open(out, "w"), indent=2)
PY
}

# --- measurement window (Prometheus) -------------------------------------------------------------------------------

AB_PF_PID=""
ab_prom_start() {
  kubectl -n "$MONITORING_NAMESPACE" get svc monitoring-kube-prometheus-prometheus >/dev/null 2>&1 \
    || die "monitoring stack not found — the measured run needs Prometheus (make mon-up)."
  kubectl -n "$MONITORING_NAMESPACE" port-forward svc/monitoring-kube-prometheus-prometheus "$PROM_LOCAL_PORT:9090" >/dev/null 2>&1 &
  AB_PF_PID=$!
  for _ in $(seq 1 30); do
    curl -sf "http://localhost:$PROM_LOCAL_PORT/-/ready" >/dev/null 2>&1 && return 0
    sleep 1
  done
  die "Prometheus port-forward on :$PROM_LOCAL_PORT did not come up."
}
ab_prom_stop() {
  [[ -n "$AB_PF_PID" ]] || return 0
  kill "$AB_PF_PID" 2>/dev/null || true
  wait "$AB_PF_PID" 2>/dev/null || true
  AB_PF_PID=""
}

# Resource usage of the target's namespace (per pod, classified) + ingress-nginx over the k6 scenario window read
# from the run's journeys-*.json, divided by the journeys served in it → resources.json next to it.
# Optional 3rd/4th args = an idle (no-traffic) window in epoch ms, read in the same call → "idle" + marginal cost.
ab_window_metrics() {
  local t="$1" run_dir="$2" idle_start_ms="${3:-}" idle_end_ms="${4:-}"
  python3 "$LIB_DIR/ab_window.py" --prom "http://localhost:$PROM_LOCAL_PORT" --ns "$(ab_ns "$t")" \
    --app-prefix "$(ab_deploy "$t")-" --run-dir "$run_dir" --target "$t" \
    ${idle_start_ms:+--idle-start-ms "$idle_start_ms"} ${idle_end_ms:+--idle-end-ms "$idle_end_ms"}
}
