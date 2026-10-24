# Prompt — the full A/B test campaign (Fable 5.1, run AFTER the re-verification session)

Paste everything between the `---8<---` markers into a fresh session started in `~/projects/forum-dotnet`.
Prerequisite: the re-verification session finished (verdict READY WITH CAVEATS, 2026-10-17 23:44). Its fixes are
deliberately NOT committed yet — the campaign records exact patches instead.

---8<---

# Task: run the complete A/B test campaign on both applications and archive EVERY result

Talk to me (Jakub) in **Polish**; every file you write is in **English**. I have forgotten many details of this
project and I may be wrong about what is needed — correct me where I am.

## MODE
- `ROUND_PLAN = demo:5,stress:1,capacity:1` (≈ 3 h 15 min). If less than ~4 h remain before 08:00 when Phase 3
  starts, use `demo:3,capacity:1` (≈ 1 h 40 min) and say so.

## Context
Master's thesis: **A** = `~/projects/forum-dotnet` (Next.js CSR SPA + .NET 10 modular monolith) vs **B** =
`~/projects/gomx` (Go SSR monolith, templ + HTMX; Hubert's branch `feature/master-thesis`). Both run in one minikube
cluster (profile `forum`) behind one ingress, driven by one k6 harness (`load/k6/`, harness `h3-2026-10-17`).
Two earlier sessions aligned and then independently re-verified both apps: verdict **READY WITH CAVEATS** — all gates
green, verify round `thesis/results/rounds/20261017-2332-verify/` valid, remaining differences documented as
deviations **D1–D11** (`thesis/results/AB-COMPARISON-v1.md` §6.2). Read first: CLAUDE.md,
`docs/architecture/AB-START-HERE.md` (findings log, §5.0 verification results, checklist §5, traps §7, roadmap §8),
`thesis/results/AB-COMPARISON-v1.md` (methodology, §5.6 per-request work, deviations, runbook), your memory notes.

**Uncommitted on purpose.** The re-verification fixes are in the working trees, not committed: A = 13 modified files +
the migration `AddOwnerActivityIndexes`; B = `app/forum/web/handlers_community.go` (+2 test files). Do not commit; the
provenance of every number comes from the patches and image digests you archive in Phase 0.

Decisions already taken for this campaign (do not reopen them): D2/D11 B's feed/search cards keep the full rendered
body (product difference, reported with numbers); D3 reads stay anonymous (logged-in variant = v2); D4 the 31-word
bank stays (selective bank = v2, needs both seeders); D10 A keeps live ACL evaluation (`has_permission`, no cache read
— security code is not touched during a comparison); Hubert's optional B optimizations (member_count COUNT, vote sum)
are NOT applied. All of them must be stated next to the numbers they affect.

## Goal
Real, reproducible, fully archived data comparing A and B: functional test results of both apps, the measured
performance round (H1 resources per journey — incl. the new idle/marginal CPU rows, H2 throughput ceiling), static
metrics (H3), and a written analysis with every observation — nothing lost, everything re-analysable later.

## Hard rules
1. **Never `git commit`/`git push`.** Never `git stash`, `checkout --`, `reset --hard` or anything else that could
   drop the uncommitted fixes.
2. **Never delete/recreate the minikube profile or wipe MinIO data** (MinIO images are no longer pullable; backup:
   `~/.cache/forum-dotnet/images/minio-images-2025-09.tar`).
3. **Measurement isolation:** while a measured k6 round runs, run NOTHING heavy on this host (no builds, test suites,
   docker containers, browsers, heavy subagents, `make tunnels`, Grafana browsing). Functional tests and static metrics
   go BEFORE or AFTER the round, never during it.
4. **Checkpoint continuously.** Create `thesis/results/campaign-<YYYYMMDD-HHMM>/` first. Update its `PROGRESS.md` after
   EVERY step (what ran, exact command, start/end, result, where the raw output is, what is next) so a crashed session
   or an exhausted budget loses nothing and a new session can resume from it. Write raw outputs (stdout/stderr logs,
   TRX/JUnit/JSON reports) into the campaign folder as you go — never only in the chat. Markdown for summaries, JSON
   for machine-readable results.
5. **Economy:** no ultracode/large workflows. At most a few subagents, only for heavy separable work (e.g. B's test
   suites, log/DB analysis), never during a measured round. Long jobs in the background; wait for completion
   notifications instead of polling. Never paste big logs into the chat — write them to files, quote the relevant lines.
6. Never mix harness versions. If something that affects measurements has to change, stop, explain, and redo the
   measured part.
7. A failing test is a RESULT: record it, investigate briefly, fix only a genuine bug with a small safe fix (then re-run
   and record both runs), otherwise document it.
8. Scripts may lack the executable bit — always call them with `bash scripts/…` / `python3 scripts/…`.

## Phase 0 — preflight + provenance (≤ 20 min) → `00-preflight/`
- Save `git status`, `git log -5`, `git diff HEAD` (as `A-working-tree.patch` / `B-working-tree.patch`) of both repos,
  and the harness version. These patches + image digests ARE the version under test.
- Make sure the deployed images contain exactly these working trees (the re-verification fixes included — in
  particular the B community-handler fix and A's `AddOwnerActivityIndexes` migration applied in the cluster DB). If in
  doubt, rebuild and redeploy both: `make mk-deploy ARGS=--seed-benchmark` and `make gomx-deploy ARGS=--reset-db`
  (deploy.sh restarts pods on dirty tags). Record image tags + digests, applied A migrations, B `/stats` version.
- `ab_check_profile a|b` all ok (helper in AB-START-HERE §5); both Postgres versions; seed counts 800/1600/9000/15000.
- Write the plan with time estimates into PROGRESS.md.

## Phase 1 — functional tests of both apps (before the round) → `01-functional/`
- **A:** `dotnet build`, full `dotnet test` with TRX loggers (unit, architecture, integration — expect 248+ passed,
  0 failed, **0 skipped**; a skip means Testcontainers failed), `dotnet format --verify-no-changes`; frontend
  `npm run typecheck`, `npm run lint`, `npm test`, `npm run build` (next/font may fetch fonts from Google — retry once on
  a transient failure; remove any stray `bin\Debug` dir left by `dotnet ef` before image builds).
- **B:** via docker (no Go on the host — recipe AB-START-HERE §5 V-2): `go vet`, `go test -race ./...` incl. the
  Postgres adapter tests, gofmt, golangci-lint 1.64.5; B's Playwright/e2e and Lighthouse suites if they can run in docker
  here (check its Taskfile/CI) — otherwise record why. Hubert's GitLab pipeline failed on `f03d46a4` without accessible
  logs; the re-verification could not reproduce it (candidates: lint:ts, Go 1.26.1 toolchain download, MR-only jobs) —
  try the remaining candidates cheaply and report.
- Per app: passed/failed/skipped, durations, coverage if cheap → `functional-summary.json`.

## Phase 2 — live API/system checks on the cluster → `02-live-checks/`
Against both deployments through the ingress: every endpoint the harness uses (status, gzip/raw size, single-shot
time), security posture (private content 404/omitted on A, OAuth mock off on B, limiter state), compression, TLS,
WebSocket handshake, health/readiness, seed sentinels via SQL (AB-START-HERE §5.1), SQL statements per journey request
if cheap (re-verification §5.6 numbers as the reference), app logs for errors/warnings, Prometheus targets UP. Save
every request/response summary. Screenshots: Claude in Chrome is probably unavailable; the headless Chrome used by
`scripts/lighthouse.sh` under `.bench/` probably is — capture feed, thread, search and profile pages of both apps on
the benchmark data into `02-live-checks/screenshots/` (do this before Phase 3, never during it).

## Phase 3 — the measured round (the core data) → `03-round/`
Gate: `make ab-round ARGS="--plan smoke:1 --label gate"` → both runs valid. Then, host otherwise idle:
`make ab-round ARGS="--plan <ROUND_PLAN> --label v1"` in the background. Record the archive path in PROGRESS.md
immediately; while it runs, only wait. Afterwards: read REPORT.md (incl. idle cores and MARGINAL CPU per journey);
rerun only profiles with invalid runs (`--plan <profile>:<n>`); check k6 CPU headroom and that the capacity knee was
reached on both sides (otherwise rerun capacity with higher `CAP_STEPS`, keeping setup + run under A's 15-min token
lifetime); collect app logs of both namespaces for the round window (errors, warnings, slow requests) and pod events.

## Phase 4 — static metrics (H3), after the round → `04-static/`
On the exact versions measured: trivy (pinned 0.72.0, same flags: `bash scripts/security-scan.sh` and
`bash scripts/security-scan.sh ../gomx`), cloc whole + core scope, image sizes and first-load bundle sizes
(`bash scripts/artifact-size.sh`), Lighthouse for both (same budgets), dependency counts, dev-loop benchmark if time
allows. Compare with the 2026-10-17 snapshot in `thesis/results/static/2026-10-17/`.

## Phase 5 — analysis and write-up → `05-analysis/` + documentation
- `CAMPAIGN-REPORT.md`: what ran, passed/failed, the key A-vs-B tables (per-journey latency, total vs MARGINAL CPU per
  journey, idle cores, memory, transfer per journey, capacity knee, WS) with mean ± 95 % CI and the deviation caveats
  (D1–D11) next to the numbers they affect; anomalies; threats to validity; what the data does and does NOT support for
  H1/H2/H3; recommendations for the next round (v2 list).
- Fill `thesis/results/AB-COMPARISON-v1.md` §9 from the round's REPORT.md (copy, cite the archive path, never type
  numbers by hand); update AB-START-HERE (state, findings log, traps), CLAUDE.md "Current state"/"Next", gomx's
  `deploy/k8s/bench/README.md` if B's procedure changed, and your memory notes.
- `index.json` in the campaign folder listing every artifact with a one-line description.
- Final message to me in Polish: short summary of results and conclusions I can present at 08:00, the list of result
  files, open problems, and what I must do next — commits of both repos (with the exact file lists), what to tell
  Hubert (his three reviews + pipeline log), and the v2 decisions.

---8<---
