#!/usr/bin/env bash
# Dev-loop benchmark (A-9) — the local developer-loop timing protocol shared with
# Architecture B (gomx `task reload-bench`, scripts/reload-bench.sh). Measures how long a
# save takes to turn into a rebuilt artifact / restarted dev loop, per step, and emits the
# SAME JSON shape as B's `.bench/reload-bench.json` so the thesis table is a single join:
#   { timestamp, git_sha, runs, results: { <step>: {min_ms, median_ms, max_ms, mean_ms} } }
#
# Steps (A-side equivalents of B's templ/go/tailwind/bun chain):
#   backend_clean_build        dotnet build after dotnet clean (packages stay restored)
#   backend_incremental_build  dotnet build after a fresh one-line edit in a mid-graph
#                              module source (Content) — the realistic save-and-rebuild
#   dotnet_test_unit           dotnet test excluding Forum.IntegrationTests (no containers)
#   dotnet_test_full           full dotnet test incl. Testcontainers boot (Postgres,
#                              RabbitMQ, MinIO) — documented as including container startup;
#                              needs Docker. BENCH_TEST_RUNS=0 skips it.
#   frontend_clean_build       next build with .next removed first
#   next_dev_cold_start        spawn `next dev` with no .next cache -> first successful
#                              GET / (includes the lazy first-page compile: the realistic
#                              time-to-first-usable-page, not just the "Ready" banner)
#   frontend_hmr               running `next dev`: append a one-line edit to
#                              src/app/page.tsx -> wall clock until the dev server logs the
#                              recompile. Includes watcher debounce — that is the point
#                              (save-to-reload as a developer experiences it). Like B's
#                              caveat on watch-mode tools, treat as an upper bound.
#
# Fresh-edit injection mirrors B's trick (append a comment so content-addressed caches
# actually re-do work); every mutated file is snapshotted and restored on exit — the
# working tree is never left dirty by the act of measuring it.
#
# Paired-protocol note (forum-spec §9): run with the SAME BENCH_RUNS on both repos, same
# machine, same day. B's script defaults to 3; the agreed protocol value is 10.
#
# Usage:
#   bash scripts/dev-loop-bench.sh                 # BENCH_RUNS=10 (protocol default)
#   BENCH_RUNS=2 bash scripts/dev-loop-bench.sh    # quick validation pass
#   BENCH_TEST_RUNS=0 ...                          # skip the (slow) full-test step
#   BENCH_COLD_RESTORE=1 ...                       # add backend_cold_restore_build: restore
#                                                  # + build against an EMPTY throwaway
#                                                  # NuGet cache (network-heavy; the global
#                                                  # cache is untouched and re-linked after)
set -uo pipefail

RUNS="${BENCH_RUNS:-10}"
TEST_RUNS="${BENCH_TEST_RUNS:-$RUNS}"
COLD_RESTORE="${BENCH_COLD_RESTORE:-0}"
DEV_PORT="${BENCH_DEV_PORT:-3101}"

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

WORK="$(mktemp -d)"   # throwaway outputs (NuGet cold cache, logs)
BK="$(mktemp -d)"     # backups of files the bench mutates
OUTDIR="$ROOT/.bench"
mkdir -p "$OUTDIR"

BACKEND_TARGET="backend/src/Modules/Content/Forum.Modules.Content/Application/Threads/CreateThread.cs"
FRONTEND_TARGET="frontend/src/app/page.tsx"
DATA="$WORK/data.tsv"
DEV_LOG="$WORK/next-dev.log"
DEV_PID=""

# --- non-destructive guards: snapshot everything we touch, restore on exit ----
cp "$BACKEND_TARGET" "$BK/backend_target.bak"
cp "$FRONTEND_TARGET" "$BK/frontend_target.bak"

cleanup() {
  if [ -n "$DEV_PID" ]; then
    kill -- "-$DEV_PID" 2>/dev/null || kill "$DEV_PID" 2>/dev/null
    wait "$DEV_PID" 2>/dev/null
  fi
  cp "$BK/backend_target.bak" "$BACKEND_TARGET" 2>/dev/null || true
  cp "$BK/frontend_target.bak" "$FRONTEND_TARGET" 2>/dev/null || true
  if [ "$COLD_RESTORE" = "1" ]; then
    # Re-point project.assets.json at the global cache the cold step displaced.
    (cd backend && unset NUGET_PACKAGES && dotnet restore Forum.slnx --force >/dev/null 2>&1) || true
  fi
  rm -rf "$WORK" "$BK"
}
trap cleanup EXIT

# --- timing helpers (bash 5 EPOCHREALTIME, microsecond resolution) ------------
timer_start() { START="$EPOCHREALTIME"; }
timer_ms() { awk -v a="$START" -v b="$EPOCHREALTIME" 'BEGIN { printf "%.1f", (b - a) * 1000 }'; }

# --- prep hooks ---------------------------------------------------------------
prep_none() { :; }
prep_backend_clean() { (cd backend && dotnet clean Forum.slnx >/dev/null 2>&1); }
# Force a real content change so MSBuild's up-to-date check re-compiles this run.
prep_backend_edit() { printf '\n// dev-loop-bench %s-%s\n' "$$" "$RANDOM" >>"$BACKEND_TARGET"; }
prep_frontend_clean() { rm -rf frontend/.next; }

# --- step commands ------------------------------------------------------------
cmd_backend_build() { (cd backend && dotnet build Forum.slnx); }
cmd_test_unit() { (cd backend && dotnet test Forum.slnx --filter 'FullyQualifiedName!~Forum.IntegrationTests'); }
cmd_test_full() { (cd backend && dotnet test Forum.slnx); }
cmd_frontend_build() { (cd frontend && npm run build); }
cmd_cold_restore_build() {
  (cd backend && NUGET_PACKAGES="$WORK/nuget" dotnet restore Forum.slnx --force \
     && NUGET_PACKAGES="$WORK/nuget" dotnet build Forum.slnx)
}
prep_cold_restore() { rm -rf "$WORK/nuget"; (cd backend && dotnet clean Forum.slnx >/dev/null 2>&1); }

# --- runner (identical stats/format to B's reload-bench.sh) -------------------
printf 'Dev-loop benchmark  (runs=%s, full-test runs=%s, warm caches)\n\n' "$RUNS" "$TEST_RUNS"
printf '%-26s %9s %9s %9s %9s\n' "step" "min" "median" "max" "mean"
printf '%-26s %9s %9s %9s %9s   (ms)\n' "----" "---" "------" "---" "----"

run() { # display  json_key  prep_fn  cmd_fn  [runs_override]
  local disp="$1" key="$2" prep="$3" cmd="$4" n="${5:-$RUNS}"
  if [ "$n" -le 0 ]; then
    printf '%-26s %s\n' "$disp" "SKIPPED (runs=0)"
    return
  fi
  # warmup (validates the tool is present; not timed)
  "$prep"
  if ! "$cmd" >/dev/null 2>&1; then
    printf '%-26s %s\n' "$disp" "SKIPPED (command failed — tool missing?)"
    return
  fi
  local samples=() i
  for ((i = 1; i <= n; i++)); do
    "$prep"
    timer_start
    "$cmd" >/dev/null 2>&1
    samples+=("$(timer_ms)")
  done
  emit_row "$disp" "$key" "${samples[@]}"
}

emit_row() { # display  json_key  samples...
  local disp="$1" key="$2"
  shift 2
  local stats
  stats="$(printf '%s\n' "$@" | sort -n | awk '
    { a[NR] = $1; sum += $1 }
    END {
      n = NR; min = a[1]; max = a[n]
      med = (n % 2) ? a[(n + 1) / 2] : (a[n / 2] + a[n / 2 + 1]) / 2
      printf "%.1f\t%.1f\t%.1f\t%.1f", min, med, max, sum / n
    }')"
  local mn md mx me
  IFS=$'\t' read -r mn md mx me <<<"$stats"
  printf '%-26s %9s %9s %9s %9s\n' "$disp" "$mn" "$md" "$mx" "$me"
  printf '%s\t%s\t%s\t%s\t%s\n' "$key" "$mn" "$md" "$mx" "$me" >>"$DATA"
}

# --- next-dev helpers ---------------------------------------------------------
wait_for_page() { # poll GET / until 200 (first hit triggers the lazy page compile)
  local deadline=$((SECONDS + 180))
  while ((SECONDS < deadline)); do
    if curl -sf -o /dev/null --max-time 5 "http://localhost:$DEV_PORT/"; then
      return 0
    fi
    sleep 0.1
  done
  return 1
}

start_dev() {
  : >"$DEV_LOG"
  # setsid: own process group, so stop_dev can kill next dev AND its server workers
  (cd frontend && exec setsid npx next dev -p "$DEV_PORT") >>"$DEV_LOG" 2>&1 &
  DEV_PID=$!
}

stop_dev() {
  if [ -n "$DEV_PID" ]; then
    kill -- "-$DEV_PID" 2>/dev/null || kill "$DEV_PID" 2>/dev/null
    wait "$DEV_PID" 2>/dev/null
    DEV_PID=""
  fi
  # next dev forks workers; make sure the port is actually free before the next run
  local deadline=$((SECONDS + 15))
  while ((SECONDS < deadline)) && curl -sf -o /dev/null --max-time 1 "http://localhost:$DEV_PORT/"; do
    sleep 0.2
  done
}

bench_cold_start() {
  local samples=() i
  for ((i = 1; i <= RUNS; i++)); do
    stop_dev
    rm -rf frontend/.next
    timer_start
    start_dev
    if ! wait_for_page; then
      printf '%-26s %s\n' "next dev cold start" "SKIPPED (dev server never served / — see $DEV_LOG)"
      stop_dev
      return 1
    fi
    samples+=("$(timer_ms)")
  done
  stop_dev
  emit_row "next dev cold start" "next_dev_cold_start" "${samples[@]}"
}

bench_hmr() {
  start_dev
  if ! wait_for_page; then
    printf '%-26s %s\n' "frontend HMR (next dev)" "SKIPPED (dev server never served /)"
    stop_dev
    return 1
  fi
  local samples=() i before after
  for ((i = 1; i <= RUNS; i++)); do
    before="$(grep -c 'Compiled' "$DEV_LOG" || true)"
    timer_start
    printf '\n// dev-loop-bench %s-%s\n' "$$" "$RANDOM" >>"$FRONTEND_TARGET"
    # wait for the dev server to log the recompile triggered by the edit
    local deadline=$((SECONDS + 60)) ok=0
    while ((SECONDS < deadline)); do
      after="$(grep -c 'Compiled' "$DEV_LOG" || true)"
      if ((after > before)); then ok=1; break; fi
      sleep 0.05
    done
    if ((ok == 0)); then
      printf '%-26s %s\n' "frontend HMR (next dev)" "SKIPPED (no recompile detected in 60s)"
      stop_dev
      return 1
    fi
    samples+=("$(timer_ms)")
  done
  stop_dev
  emit_row "frontend HMR (next dev)" "frontend_hmr" "${samples[@]}"
}

# --- execute ------------------------------------------------------------------
run "backend clean build"       "backend_clean_build"       prep_backend_clean cmd_backend_build
run "backend incremental build" "backend_incremental_build" prep_backend_edit  cmd_backend_build
run "dotnet test (unit only)"   "dotnet_test_unit"          prep_none          cmd_test_unit
run "dotnet test (full)"        "dotnet_test_full"          prep_none          cmd_test_full "$TEST_RUNS"
if [ "$COLD_RESTORE" = "1" ]; then
  run "backend cold restore+build" "backend_cold_restore_build" prep_cold_restore cmd_cold_restore_build
fi
run "frontend clean build"      "frontend_clean_build"      prep_frontend_clean cmd_frontend_build
bench_cold_start || true
bench_hmr || true

# --- write JSON (identical schema to gomx .bench/reload-bench.json) -----------
SHA="$(git rev-parse --short HEAD 2>/dev/null || echo unknown)"
TS="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
OUT="$OUTDIR/reload-bench.json"
awk -v ts="$TS" -v sha="$SHA" -v runs="$RUNS" '
  BEGIN { FS = "\t" }
  { rows[NR] = $0 }
  END {
    print "{"
    printf "  \"timestamp\": \"%s\",\n", ts
    printf "  \"git_sha\": \"%s\",\n", sha
    printf "  \"runs\": %s,\n", runs
    print "  \"results\": {"
    for (i = 1; i <= NR; i++) {
      split(rows[i], c, FS)
      printf "    \"%s\": {\"min_ms\": %s, \"median_ms\": %s, \"max_ms\": %s, \"mean_ms\": %s}%s\n", \
        c[1], c[2], c[3], c[4], c[5], (i < NR ? "," : "")
    }
    print "  }"
    print "}"
  }' "$DATA" >"$OUT"

printf '\nwrote %s\n' "$OUT"
if [ "$TEST_RUNS" != "$RUNS" ]; then
  printf 'note: dotnet_test_full used %s run(s), not %s — record that in the protocol table.\n' "$TEST_RUNS" "$RUNS"
fi
