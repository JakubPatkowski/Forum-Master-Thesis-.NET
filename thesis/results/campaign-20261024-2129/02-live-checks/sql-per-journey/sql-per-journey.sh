#!/usr/bin/env bash
# Campaign v3 — Phase 4.2 SQL statements per journey on the live cluster databases (method of
# campaign-20261017-2357/02-live-checks/sql-per-journey): log_statement=all on both Postgres pods for a short window,
# psql markers around ONE request set per journey (after two warm-up passes), statements classified; logging reset and
# verified 'none' afterwards. Run on a pristine reseed. Usage: sql-per-journey.sh <out dir>
set -u
OUT="$1"; mkdir -p "$OUT"
S=/tmp/claude-1000/-home-ubuntuwsl-projects-forum-dotnet/699989b2-0c93-4422-861f-c87a329e1be6/scratchpad
IP="$(minikube -p forum ip)"; CA="$(mkcert -CAROOT)/rootCA.pem"
C=(curl -sS -o /dev/null --cacert "$CA" --resolve "forum.local:443:$IP" --resolve "gomx.local:443:$IP" --resolve "minio.forum.local:443:$IP" --max-time 30 -H 'Accept-Encoding: gzip')
CJ=(curl -sS --cacert "$CA" --resolve "forum.local:443:$IP" --resolve "gomx.local:443:$IP" --resolve "minio.forum.local:443:$IP" --max-time 30)
A=https://forum.local; B=https://gomx.local
apsql() { kubectl -n forum-dotnet exec postgres-0 -- psql -U forum -d forum_net -tAc "$1"; }
bpsql() { kubectl -n gomx exec postgres-0 -- psql -U gomx -d gomx -tAc "$1"; }
mark() { apsql "SELECT 'MARK $1'" >/dev/null; bpsql "SELECT 'MARK $1'" >/dev/null; }
LOG="$OUT/run.log"; echo "# started $(date -Iseconds)" > "$LOG"

# ids + credentials (outside the logging window)
CAT06="$(apsql "SELECT id FROM forum_content.categories WHERE slug='bench-cat-06'")"
CAT01="$(apsql "SELECT id FROM forum_content.categories WHERE slug='bench-cat-01'")"
PUB_T=01KDY021004XKGMPA2XJZK3DPT
TYP_T="$(apsql "SELECT t.id FROM forum_content.threads t JOIN forum_content.categories c ON c.id=t.category_id LEFT JOIN forum_content.comments cm ON cm.thread_id=t.id WHERE c.visibility='public' AND NOT t.is_deleted GROUP BY t.id HAVING count(cm.*)=4 ORDER BY t.id LIMIT 1")"
TYP_B="$(bpsql "SELECT p.id FROM posts p JOIN communities c ON c.id=p.community_id LEFT JOIN comments cm ON cm.post_id=p.id WHERE c.visibility='public' GROUP BY p.id HAVING count(cm.*)=4 ORDER BY p.id LIMIT 1")"
UID34="$(apsql "SELECT id FROM forum_identity.users WHERE username='bench_user_0034'")"
TOKEN="$("${CJ[@]}" -H 'Content-Type: application/json' -d '{"email":"bench_user_0034@bench.local","password":"Bench#Password1"}' "$A/api/identity/login" | python3 -c 'import json,sys; print(json.load(sys.stdin)["accessToken"])')"
COOKIE="$("${CJ[@]}" -o /dev/null -D - -X POST -d 'username=benchuser0034&password=DemoPassword1' "$B/login" | sed -nE 's/^[Ss]et-[Cc]ookie: (forum_session=[^;]*).*/\1/p' | tr -d '\r')"
PEER="$("${CJ[@]}" -H "Authorization: Bearer $TOKEN" "$A/api/social/friends?limit=50" | python3 -c 'import json,sys; print(json.load(sys.stdin)["items"][0]["userId"])')"
BPEER="$("${CJ[@]}" -H "Cookie: $COOKIE" "$B/messages" | grep -oE '/messages/[0-9]+' | head -1 | cut -d/ -f3)"
echo "A jwt ${#TOKEN} chars, B cookie ${#COOKIE} chars; typical threads A=$TYP_T B=$TYP_B (4 comments); peers A=$PEER B=$BPEER" | tee -a "$LOG"
PNG="$S/photo.png"; PNG_SIZE="$(stat -c %s "$PNG")"
AH=(-H "Authorization: Bearer $TOKEN"); BH=(-H "Cookie: $COOKIE")
feed_ids() { "${CJ[@]}" "$A/api/content/threads?categoryId=$CAT06&limit=20" | python3 -c 'import json,sys; print(",".join(i["id"] for i in json.load(sys.stdin)["items"]))'; }
tree_ids() { "${CJ[@]}" "$A/api/content/threads/$1/comments" | python3 -c 'import json,sys; d=json.load(sys.stdin); it=d if isinstance(d,list) else d.get("items",[]); print(",".join(sorted(i["id"] for i in it)[:100]))'; }
FEED_IDS="$(feed_ids)"; TREE_PUB="$(tree_ids $PUB_T)"; TREE_TYP="$(tree_ids $TYP_T)"

j_nav()  { "${C[@]}" "$A/api/content/categories"; "${C[@]}" "$B/communities"; }
j_j1()   { "${C[@]}" "$A/api/content/threads?categoryId=$CAT06&limit=20"; "${C[@]}" "$A/api/engagement/reactions/batch?targetType=thread&targetIds=$FEED_IDS"; "${C[@]}" "$B/c/general"; }
j_j2h()  { "${C[@]}" "$A/api/content/threads/$PUB_T" & "${C[@]}" "$A/api/content/threads/$PUB_T/comments"; wait; "${C[@]}" "$A/api/engagement/reactions/batch?targetType=comment&targetIds=$TREE_PUB"; "${C[@]}" "$B/thread/1"; }
j_j2t()  { "${C[@]}" "$A/api/content/threads/$TYP_T" & "${C[@]}" "$A/api/content/threads/$TYP_T/comments"; wait; "${C[@]}" "$A/api/engagement/reactions/batch?targetType=comment&targetIds=$TREE_TYP"; "${C[@]}" "$B/thread/$TYP_B"; }
j_j6()   { "${C[@]}" "$A/api/content/search?q=beacon&limit=20"; "${C[@]}" "$B/search?q=beacon"; }
j_j7()   { "${C[@]}" "$A/api/engagement/users/$UID34/stats" & "${C[@]}" "$A/api/content/users/$UID34/threads"; wait; "${C[@]}" "$B/u/34"; }
j_j3()   { "${C[@]}" "${AH[@]}" -H 'Content-Type: application/json' -d '{"parentId":null,"body":"seeded sql count comment"}' "$A/api/content/threads/$TYP_T/comments"; "${C[@]}" "${BH[@]}" -d 'body=seeded sql count comment' "$B/posts/$TYP_B/comments"; }
j_j5()   { "${C[@]}" -X PUT "${AH[@]}" "$A/api/engagement/reactions/thread/$TYP_T"; "${C[@]}" "${BH[@]}" -H 'HX-Request: true' -d 'dir=up' "$B/posts/$TYP_B/vote"; }
j_j5b()  { "${C[@]}" -X DELETE "${AH[@]}" "$A/api/engagement/reactions/thread/$TYP_T"; "${C[@]}" "${BH[@]}" -H 'HX-Request: true' -d 'dir=up' "$B/posts/$TYP_B/vote"; }
j_j4()   {
  local init up fid tid
  init="$("${CJ[@]}" "${AH[@]}" -H 'Content-Type: application/json' -d "{\"contentType\":\"image/png\",\"sizeBytes\":$PNG_SIZE}" "$A/api/files")"
  up="$(python3 -c 'import json,sys; print(json.loads(sys.argv[1])["uploadUrl"])' "$init")"; fid="$(python3 -c 'import json,sys; print(json.loads(sys.argv[1])["fileId"])' "$init")"
  curl -sS -o /dev/null --cacert "$CA" --resolve "minio.forum.local:443:$IP" -X PUT -H 'Content-Type: image/png' --data-binary "@$PNG" "$up"
  "${C[@]}" -X POST "${AH[@]}" "$A/api/files/$fid/commit"
  tid="$("${CJ[@]}" "${AH[@]}" -H 'Content-Type: application/json' -d "{\"categoryId\":\"$CAT01\",\"title\":\"sql count photo\",\"body\":\"seeded\\n\\n![photo](image:$fid)\\n\",\"tagSlugs\":[]}" "$A/api/content/threads" | python3 -c 'import json,sys; d=json.load(sys.stdin); print(d.get("threadId") or d.get("id"))')"
  "${C[@]}" "${AH[@]}" -H 'Content-Type: application/json' -d "{\"targetType\":\"thread\",\"targetId\":\"$tid\"}" "$A/api/files/$fid/attachments"
  local u st blob; u="$("${CJ[@]}" "${BH[@]}" -F "file=@$PNG;type=image/png;filename=photo.png" "$B/uploads" | python3 -c 'import json,sys; d=json.load(sys.stdin); print(d.get("upload_id") or d.get("id"))')"
  for _ in $(seq 1 20); do st="$("${CJ[@]}" "${BH[@]}" "$B/uploads/$u/status")"; [[ "$st" == *completed* ]] && break; sleep 1; done
  blob="$(python3 -c 'import json,sys; print(json.loads(sys.argv[1])["result"]["id"])' "$st")"
  "${C[@]}" "${BH[@]}" --data-urlencode community=showcase --data-urlencode "title=sql count photo" --data-urlencode "body=seeded

![](/uploads/$blob)
" "$B/posts"
}
j_j9()   {
  local conv; conv="$("${CJ[@]}" "${AH[@]}" -H 'Content-Type: application/json' -d "{\"userId\":\"$PEER\"}" "$A/api/social/conversations/direct" | python3 -c 'import json,sys; print(json.load(sys.stdin)["conversationId"])')"
  "${C[@]}" "${AH[@]}" -H 'Content-Type: application/json' -d '{"body":"seeded sql count dm"}' "$A/api/social/conversations/$conv/messages"
  "${C[@]}" "${BH[@]}" -d 'body=seeded sql count dm' "$B/messages/$BPEER"
}
# two warm-up passes (JIT, pools, prepared statements) BEFORE logging
for _ in 1 2; do j_nav; j_j1; j_j2h; j_j2t; j_j6; j_j7; done

apsql "ALTER SYSTEM SET log_statement = 'all'" >/dev/null; apsql "SELECT pg_reload_conf()" >/dev/null
bpsql "ALTER SYSTEM SET log_statement = 'all'" >/dev/null; bpsql "SELECT pg_reload_conf()" >/dev/null
sleep 2
echo "log_statement now: A=$(apsql 'SHOW log_statement') B=$(bpsql 'SHOW log_statement')" | tee -a "$LOG"
START="$(date -u +%Y-%m-%dT%H:%M:%SZ)"; sleep 1
for j in nav j1 j2h j2t j6 j7 j3 j5 j5b j4 j9; do
  mark "$j-start"; sleep 1; "j_$j"; sleep 2; mark "$j-end"; sleep 1
done
mark "idle-start"; sleep 20; mark "idle-end"
apsql "ALTER SYSTEM RESET log_statement" >/dev/null; apsql "SELECT pg_reload_conf()" >/dev/null
bpsql "ALTER SYSTEM RESET log_statement" >/dev/null; bpsql "SELECT pg_reload_conf()" >/dev/null
sleep 2
echo "log_statement after reset: A=$(apsql 'SHOW log_statement') B=$(bpsql 'SHOW log_statement') (must be 'none')" | tee -a "$LOG"
kubectl -n forum-dotnet logs postgres-0 --since-time="$START" > "$OUT/A-postgres.log" 2>&1
kubectl -n gomx logs postgres-0 --since-time="$START" > "$OUT/B-postgres.log" 2>&1
python3 - "$OUT" <<'PY' | tee "$OUT/summary.txt"
import json, os, re, sys
out = sys.argv[1]
LABEL = {"nav": "nav (A categories | B /communities)", "j1": "J1 page 1 (A feed + batch | B /c/general)",
         "j2h": "J2 heaviest thread (A detail‖tree + batch | B /thread/1)", "j2t": "J2 typical thread, 4 comments",
         "j6": "J6 search 'beacon' (rank 50)", "j7": "J7 profile (A stats‖threads | B /u/34)",
         "j3": "J3 comment create (+relay window)", "j5": "J5 reaction add / vote", "j5b": "J5 reaction remove / vote back",
         "j4": "J4 thread + photo (A presign/commit/thread/attach | B upload/poll/post)", "j9": "J9 DM (A open+send | B send)",
         "idle": "idle 20 s (no traffic)"}
BG_A = [("Npgsql pooled-connection reset (D9)", r"SET SESSION AUTHORIZATION DEFAULT;RESET ALL"),
        ("tx control (BEGIN/COMMIT/ROLLBACK)", r"^(BEGIN|COMMIT|ROLLBACK)\b"),
        ("outbox relay poll", r"outbox_messages"), ("inbox (consumer dedupe)", r"inbox_messages"),
        ("health check SELECT 1", r"^SELECT 1$"), ("postgres-exporter (monitoring)", r"pg_stat_|pg_settings|pg_database|pg_replication|pg_postmaster"),
        ("psql marker/meta", r"MARK |^SHOW |pg_reload_conf|ALTER SYSTEM"),
        ("files orphan sweep / advisory", r"pg_try_advisory|pg_advisory_unlock")]
BG_B = [("River job queue poll", r"river_job"), ("River leader election", r"river_leader"), ("River queue/notify", r"river_queue|river_notification|pg_notify|LISTEN"),
        ("pgx pool ping", r"^-- ping"), ("tx control (begin/commit/rollback)", r"^(begin|commit|rollback|BEGIN|COMMIT|ROLLBACK)\b"),
        ("psql marker/meta", r"MARK |^SHOW |pg_reload_conf|ALTER SYSTEM"), ("postgres-exporter (monitoring)", r"pg_stat_|pg_settings")]
def statements(path):
    st = []
    for line in open(path, errors="replace"):
        m = re.match(r"^(\S+ \S+) UTC \[(\d+)\] LOG:  (?:statement|execute [^:]*): (.*)$", line.rstrip("\n"))
        if m:
            st.append(m.group(3).strip())
    return st
res = {}
for side, bg in (("A", BG_A), ("B", BG_B)):
    st = statements(os.path.join(out, f"{side}-postgres.log"))
    marks = [i for i, s in enumerate(st) if s.startswith("SELECT 'MARK ")]
    res[side] = {}
    print(f"== {side}: {len(st)} logged statements; marks found: {len(marks)}")
    for key, label in LABEL.items():
        try:
            i0 = next(i for i, s in enumerate(st) if s == f"SELECT 'MARK {key}-start'")
            i1 = next(i for i, s in enumerate(st) if s == f"SELECT 'MARK {key}-end'")
        except StopIteration:
            continue
        fg, bgc = [], {}
        for s in st[i0 + 1:i1]:
            cls = next((name for name, rx in bg if re.search(rx, s)), None)
            if cls:
                bgc[cls] = bgc.get(cls, 0) + 1
            else:
                fg.append(s[:150])
        res[side][label] = {"foreground": len(fg), "background": bgc, "foreground_statements": fg}
        print(f"  {label:<70} foreground={len(fg):>3}  background={sum(bgc.values()):>3} {bgc}")
        for s in fg:
            print(f"      · {s}")
json.dump(res, open(os.path.join(out, "summary.json"), "w"), indent=2)
PY
echo "# finished $(date -Iseconds)" >> "$LOG"
