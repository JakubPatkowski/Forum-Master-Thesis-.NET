# Prompt — independent re-verification of the A/B comparison (Fable 5.1, new Claude Code session)

Paste everything between the two `---8<---` markers into a fresh session started in `~/projects/forum-dotnet`.
Set the two MODE lines first. The prompt is deliberately written against *claims*, not facts: the new session must
re-derive everything from code, data and live measurements.

---8<---

# Task: independent re-verification of the A/B thesis comparison (forum-dotnet vs gomx) — find and fix whatever still makes it unfair, unreliable or broken

Talk to me (Jakub, the owner) in **Polish**. Every file you write in either repository is in **English** (repo rule).
I have been away from this project for about two months and have forgotten most of the details: whenever you report a
finding, explain in one or two plain sentences what it means and why it matters for the comparison. If any of my
assumptions is wrong, correct me directly.

## MODE (edit before pasting)
- `RUN_ROUND = ask` — after verification, fixes and gates, STOP and ask me before the ~3 h measured round.
  (`auto` = start it yourself once every gate is green; `no` = verification and fixes only.)
- `ROUND_PLAN = demo:5,stress:1,capacity:1` (≈ 3 h). If time is short: `demo:3,capacity:1` (≈ 1.5 h).

## What this is
My master's thesis compares two complete implementations of the same forum at the **system level**:
**A** = `~/projects/forum-dotnet` (React/Next.js CSR SPA + .NET 10 modular-monolith JSON API; my repo, branch
`29-feat-phase-12---tauri-desktop-application`) and **B** = `~/projects/gomx` (Go SSR monolith with templ + HTMX;
Hubert Ożarowski's repo, branch `feature/master-thesis`). Hypotheses: H1 resources per served user journey at equal
load, H2 throughput ceiling (journey p95 ≤ 500 ms), H3 static metrics. Both run in ONE minikube cluster (profile
`forum`; namespaces `forum-dotnet` and `gomx`; shared ingress-nginx; A's Prometheus/Grafana in `monitoring`) and are
driven by ONE k6 harness in A's repo (`load/k6/`, adapters `targets/a.js` and `targets/b.js`). Paradigm differences
(HTML vs JSON, sqlc vs EF Core, int64 vs ULID, ILIKE vs tsvector, Redis vs RabbitMQ+outbox, sessions vs JWT, server
ingest vs presigned upload) are the subject of study and stay; everything else must be equal or documented.

On 2026-10-17 a previous session reviewed Hubert's branch, fixed both apps and the harness, built an unattended round
orchestrator and declared both sides "ready for the first measured round" (dry run passed). I need the first measured
round with saved data and a v1 comparison document for a class on **2026-10-18 at 08:00**. Check `git log`/`git status`
in both repos: I may already have committed that work (A on top of `ee0faee`, B on top of `e13f18f`) — review the
alignment diff either way.

## Your stance
- You are an **independent auditor and fixer**. Treat everything the previous session wrote — CLAUDE.md, your memory
  notes, `docs/architecture/AB-START-HERE.md`, `thesis/results/AB-COMPARISON-v1.md`, code comments, numbers in docs —
  as **claims to verify**, not facts. Re-derive them from code, data and live measurements.
- Decide whether A and B are ready for a **fair, reliable and meaningful** comparison; fix every objective problem;
  record every difference that stays.
- Hunt for asymmetries nobody has listed yet. The last review found 15+ problems earlier reviews had missed (findings
  log in AB-START-HERE §4.2) — assume more exist.
- Prioritize by impact: anything that would invalidate or bias measured numbers first; nice-to-haves last.

## Hard rules
1. **Never `git commit` or `git push`** (CLAUDE.md). Stage only; I commit. Changes to B reach Hubert's branch through me.
2. **Never delete/recreate the minikube profile or wipe MinIO data.** MinIO images can no longer be pulled from any
   registry; the only copy is in the cluster and in `~/.cache/forum-dotnet/images/minio-images-2025-09.tar`.
3. No code or config changes while a measured round runs; never run two load tests at once; keep `make tunnels` and
   Grafana browsing off during measurements.
4. Changes to B (gomx): minimal, in gomx's style, with tests; verify with `go vet`, `go test`, gofmt and
   golangci-lint 1.64.5 through docker (there is no Go toolchain on the host — recipe in AB-START-HERE §5 V-2); list
   every B change for Hubert.
5. Keep paradigm differences. Do not reverse the documented decisions D1–D8 (AB-COMPARISON-v1 §6.2) without asking
   me; if evidence shows one is wrong, explain and propose.
6. Run long operations (image builds, test suites, rounds) in the background; never block on them.
7. If you change adapter behaviour, bump `HARNESS_VERSION` in `load/k6/main.js` (results of different harness
   versions must never be mixed).
8. You may use parallel read-only subagents for the code audit of A, B and the harness — verify every claim they make
   yourself before acting on it.

## Phase 1 — orient (≤ 20 min)
Read: `docs/architecture/AB-START-HERE.md` (document map, findings log, checklist §5, traps §7, roadmap §8),
`thesis/results/AB-COMPARISON-v1.md`, `docs/architecture/AB-ALIGNMENT-INSTRUCTIONS-A.md` §1–§4 and §6,
`docs/specs/forum-spec.md` (DRAFT), `thesis/results/DAY0-2026-10-17.md` §0. Then the code that decides the numbers:
`load/k6/main.js`, `load/k6/targets/{a,b}.js`, `load/k6/lib/*.js`,
`scripts/{lib-bench.sh,ab-round.sh,ab_window.py,ab_report.py,run-load-test.sh,gomx-bench-deploy.sh,deploy.sh}`,
A's Content/Engagement read and write paths, gomx's `app/forum/web/handlers_*.go`, `app/forum/shared/seedbench.go`,
`app/forum/shared/postgres/`, `deploy/k8s/bench/`. Record the baseline: branches, HEADs, dirty state, cluster state,
deployed image digests. Post a short plan (do not wait for an answer) and continue.

## Phase 2 — verify (the core)
Execute AB-START-HERE §5 V-0 … V-12 yourself and compare every result with the expected value. Then go deeper — at least:
a) **Functional equivalence per journey** (J1, J2, J3, J5, J6, J7, nav, J8): what A's real SPA (frontend code) and B's
   real browser flow (htmx) do vs what the k6 adapters do. Quantify D1 (calls the real SPA makes per page vs the adapter).
b) **Work per request**: count the SQL statements behind one request of each journey on both sides — use local docker
   copies of both stacks with `pg_stat_statements` / `log_statement=all` on a scratch database (do NOT change the
   cluster's Postgres config) — and look for N+1s or extra queries on one side.
c) **Query plans**: `EXPLAIN (ANALYZE, BUFFERS)` for the feed page, thread + comment tree, search and profile queries on
   both — the same index quality on both sides?
d) **Response size** per journey (gzip and raw) and what content it actually carries.
e) **Data**: run AB-START-HERE §5.1 on both freshly reseeded databases (comment distribution, private/public split,
   search match sets, reaction split, pinned, body length); confirm that two reseeds give an identical shape.
f) **Harness and analysis math**: measured window = scenario only; journeys/s denominators; capacity step tagging and
   the knee rule; report statistics; validity rules; no credentials archived; ABBA ordering; warm-up effects.
g) **Environment**: common profile live on both; identical Postgres; probes; runtime/GC knobs (D8); CPU throttling;
   background jobs inside the app pods; ingress, TLS, HTTP versions; k6 CPU headroom at the capacity steps.
h) **Tests and CI**: A's integration suite really runs (0 skipped) — and what GitHub CI does without the MinIO image;
   B's CI expectations (templ fmt, generated code) for the edits on its branch.
i) Anything else that looks suspicious — say how it could bias H1 or H2.

## Phase 3 — fix
Fix every objective bug or asymmetry with the smallest correct change (tests where code changes). For each finding
record: what was wrong, the evidence, the fix, how you verified it. Update the documentation: AB-START-HERE (findings
log, checklist, traps), AB-COMPARISON-v1 (§5–§7, §11, §12), `docs/architecture/AB-SCOPE-ALIGNMENT-2026-10.md`,
CLAUDE.md "Current state"/"Next", and your memory notes.

## Phase 4 — gates (all must pass before any measured run)
A: `dotnet build`, full `dotnet test` with 0 failed and **0 skipped**, `dotnet format --verify-no-changes`. B: vet, test,
gofmt, golangci-lint; `kubectl kustomize deploy/k8s/bench` renders. Redeploy whatever changed
(`make mk-deploy ARGS=--seed-benchmark` / `make gomx-deploy ARGS=--reset-db`). `ab_check_profile` all `ok` on both
sides; smoke on both with 0 % failures; `make ab-round ARGS="--plan smoke:1 --label verify"` → both runs valid and
REPORT.md generated.

## Phase 5 — verdict (to me, in Polish)
Readiness: **READY / READY WITH CAVEATS / NOT READY**. A table of findings (severity, A/B/harness, fixed or not, how,
where). The remaining deviations and their effect on H1/H2. What I must do (commits, what to tell Hubert, decisions
only I can make). What a *full* comparison still needs beyond v1.

## Phase 6 — measured round (according to RUN_ROUND)
`make ab-round ARGS="--plan <ROUND_PLAN> --label v1"` in the background. When it ends: read REPORT.md (rerun only the
profiles with invalid runs), check k6 CPU headroom and that the capacity knee was reached on both sides (otherwise
rerun capacity with higher `CAP_STEPS`, staying inside A's 15-minute access-token lifetime: setup + run < 15 min).
Copy the key tables into AB-COMPARISON-v1 §9 with the archive path, and give me a short Polish summary of the
results I can present at 08:00.

---8<---
