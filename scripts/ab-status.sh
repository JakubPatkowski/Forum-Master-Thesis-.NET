#!/usr/bin/env bash
# Status of the latest (or a given) A/B round — read-only, safe to call while a round runs (h6, 2026-10-24).
#
#   scripts/ab-status.sh                                  # the round in thesis/results/rounds/LATEST
#   scripts/ab-status.sh thesis/results/rounds/<dir>      # a specific archive
#   make ab-status
#
# Prints the round's status.json (state RUNNING|DONE|FAILED, current run, progress, variant), the age of the last
# heartbeat (ab-round.sh refreshes it every 60 s — a RUNNING round whose heartbeat is older than 15 min is dead:
# the owning session ended; continue it with `ab-round.sh --resume <dir> --plan …`), whether an ab-round.sh
# process exists, and the last log lines. Exit code: 0 = DONE, 10 = RUNNING, 20 = FAILED, 30 = STALLED.
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"
load_env

ROUND="${1:-}"
if [[ -z "$ROUND" ]]; then
  ROUND="$(cat "$REPO_ROOT/thesis/results/rounds/LATEST" 2>/dev/null || true)"
fi
[[ -n "$ROUND" && -d "$ROUND" ]] || die "no round found — pass the archive directory, or start one (make ab-round)."
[[ -f "$ROUND/status.json" ]] || die "$ROUND has no status.json (archive older than harness h6 — read its round.log)."

python3 - "$ROUND/status.json" <<'PY'
import json, sys, time
d = json.load(open(sys.argv[1]))
age = int(time.time()) - int(d.get("heartbeat_epoch") or d.get("updated_epoch") or 0)
state = d.get("state", "?")
stalled = state == "RUNNING" and age > 15 * 60
print(f"round     : {d.get('round_dir')}")
print(f"state     : {state}{'  <-- STALLED (no heartbeat for %d min; the process is gone — resume it)' % (age // 60) if stalled else ''}")
print(f"plan      : {d.get('plan')}")
print(f"progress  : {d.get('runs_done')}/{d.get('runs_total')} runs done, {d.get('runs_valid')} valid")
print(f"current   : {d.get('current_run') or '-'}")
print(f"detail    : {d.get('detail') or '-'}")
print(f"updated   : {d.get('updated')} (heartbeat {age} s ago, pid {d.get('pid')})")
sys.exit(30 if stalled else {"DONE": 0, "RUNNING": 10, "FAILED": 20}.get(state, 20))
PY
rc=$?
echo "--- processes"
pgrep -af "scripts/ab-round.sh" | grep -v pgrep || echo "no ab-round.sh process on this host"
echo "--- last log lines ($ROUND/round.log)"
tail -n 12 "$ROUND/round.log" 2>/dev/null || true
exit "$rc"
