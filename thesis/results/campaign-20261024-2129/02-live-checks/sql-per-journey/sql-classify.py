#!/usr/bin/env python3
"""Campaign v3 — re-classify the SQL-per-journey Postgres logs (log_statement=all) with connection-aware rules.

Improvements over the first pass: multi-line statements are joined (continuation lines start with a TAB); every
statement of a backend PID that ever ran a monitoring/meta query (postgres-exporter, psql markers) is classified as
monitoring/meta; Npgsql's pooled-connection reset is matched in both forms (7-statement batch and DISCARD ALL); B's River
statements are matched by their sqlc names; River job ENQUEUE/COMPLETION and the media worker's statements are counted as
journey-caused work (they exist only because J4 ran). Usage: sql-classify.py <dir with A-postgres.log, B-postgres.log>
"""
import json, os, re, sys

d = sys.argv[1]
LABEL = {"nav": "nav (A categories | B /communities)", "j1": "J1 page 1 (A feed + batch | B /c/general)",
         "j2h": "J2 heaviest thread (A detail‖tree + batch | B /thread/1)", "j2t": "J2 typical thread (4 comments)",
         "j6": "J6 search 'beacon' (rank 50)", "j7": "J7 profile (A stats‖threads | B /u/34)",
         "j3": "J3 comment create (+2 s)", "j5": "J5 reaction add / vote", "j5b": "J5 reaction remove / vote back",
         "j4": "J4 thread + photo", "j9": "J9 DM (A open+send | B send)", "idle": "idle 20 s (no traffic)"}
MON = re.compile(r"pg_stat_|pg_settings|pg_roles|pg_database|pg_replication|pg_postmaster|version\(\)|pg_is_in_recovery|pg_last_|pg_ls_waldir|pg_current_wal")
META = re.compile(r"MARK |^SHOW |pg_reload_conf|ALTER SYSTEM")
COMMON = [("Npgsql pooled-connection reset (D9)", re.compile(r"SET SESSION AUTHORIZATION DEFAULT;RESET ALL|^DISCARD ALL$")),
          ("tx control", re.compile(r"^(BEGIN|COMMIT|ROLLBACK|begin|commit|rollback)\b")),
          ("health check SELECT 1", re.compile(r"^SELECT 1$")),
          ("pgx pool ping", re.compile(r"^-- ping"))]
BG_A = [("outbox relay poll", re.compile(r"outbox_messages")), ("consumer inbox", re.compile(r"inbox_messages")),
        ("advisory lock (sweeper)", re.compile(r"pg_try_advisory|pg_advisory_unlock"))]
RIVER_BG = re.compile(r"^-- name: (JobGetAvailable|JobSchedule|Leader\w+|JobDeleteBefore|JobGetStuck|QueueDeleteExpired|QueuePoll\w*|JobRescue\w*|QueueCreateOrSetUpdatedAt|PGAdvisoryXactLock)\b")
RIVER_JOURNEY = re.compile(r"^-- name: (JobInsertFastMany|PGNotifyMany|JobSetStateIfRunningMany|JobInsert\w*)\b")
EF_SAVEPOINT = re.compile(r"SAVEPOINT")

def statements(path):
    out, cur = [], None
    for line in open(path, errors="replace"):
        line = line.rstrip("\n")
        m = re.match(r"^\S+ \S+ UTC \[(\d+)\] (LOG|DETAIL|ERROR|STATEMENT|HINT|WARNING):  ?(.*)$", line)
        if m:
            if cur: out.append(cur); cur = None
            pid, kind, rest = m.group(1), m.group(2), m.group(3)
            m2 = re.match(r"^(?:statement|execute [^:]*): (.*)$", rest)
            if kind == "LOG" and m2:
                cur = [pid, m2.group(1)]
        elif line.startswith("\t") and cur:
            cur[1] += "\n" + line.strip()
    if cur: out.append(cur)
    return out

res = {}
for side in ("A", "B"):
    st = statements(os.path.join(d, f"{side}-postgres.log"))
    mon_pids = {pid for pid, s in st if MON.search(s)}
    meta_pids = {pid for pid, s in st if META.search(s)}
    marks = {s.split("MARK ")[1].rstrip("'"): i for i, (pid, s) in enumerate(st) if s.startswith("SELECT 'MARK ")}
    res[side] = {}
    for key, label in LABEL.items():
        i0, i1 = marks.get(f"{key}-start"), marks.get(f"{key}-end")
        if i0 is None or i1 is None:
            continue
        fg, bg = [], {}
        for pid, s in st[i0 + 1:i1]:
            first = s.split("\n", 1)[0].strip()
            if pid in meta_pids:
                cls = "psql marker/meta"
            elif pid in mon_pids:
                cls = "monitoring (postgres-exporter)"
            else:
                cls = next((n for n, rx in COMMON if rx.search(first)), None)
                if not cls and side == "A":
                    cls = next((n for n, rx in BG_A if rx.search(s)), None)
                if not cls and side == "B" and RIVER_BG.search(first):
                    cls = "River poll/maintenance"
            if cls:
                bg[cls] = bg.get(cls, 0) + 1
            else:
                fg.append(first[:140] if not first.startswith("-- name:") else first)
        res[side][label] = {"foreground": len(fg), "foreground_statements": fg, "background": bg}
json.dump(res, open(os.path.join(d, "summary-classified.json"), "w"), indent=2)
with open(os.path.join(d, "summary-classified.txt"), "w") as f:
    for side in ("A", "B"):
        f.write(f"== {side}\n")
        for label, v in res[side].items():
            f.write(f"  {label:<62} foreground={v['foreground']:>3}  background={sum(v['background'].values()):>3} {v['background']}\n")
            for s in v["foreground_statements"]:
                f.write(f"      · {s}\n")
print(open(os.path.join(d, "summary-classified.txt")).read())
