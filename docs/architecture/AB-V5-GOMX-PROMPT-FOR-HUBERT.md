# Prompt for Hubert's Claude Code session (gomx): B side of the v5 A/B rounds

> Hubert: paste everything below the line into Claude Code opened in your `gomx` checkout. Written 2026-11-06 by
> the A side after reviewing `feature/master-thesis` (`thesis-v4-b`), `feature/improvements-libraries` (`b7f8fca`)
> and your rounds of 10-29…11-02. Jakub runs the matching A-side session in `forum-dotnet` at the same time.

---

You are working in **gomx**, Architecture B of a master's thesis that benchmarks it against Architecture A (a
.NET 10 + React SPA forum, repo `forum-dotnet`, Jakub's). Both authors agreed on a v5 measurement round. Your job:
prepare B's measured build. Hubert reviews and commits; you do not.

## Rules

- **Do not commit, tag or push.** Stage the changes and hand over a summary; Hubert commits, tags and pushes.
- Do not edit the `forum-dotnet` repository, even if it is checked out on this machine (you may read it).
- Follow `docs/BENCHMARK-GUARDRAILS.md` and its §6 gates. Do not merge `master` or `feature/admin-panel`.
- Change nothing on the measured surface beyond the tasks below. In particular do not touch the cache TTLs,
  the invalidation rules, the seed, the word bank or the bench resources.

## The shared contract (binding for both sides)

The full text is `docs/specs/READ-CACHE-CONTRACT.md` in `forum-dotnet` (Jakub's branch
`33-feat-phase-14---ab-v5-alignment` once pushed). Its rules, derived from `b7f8fca` as it is today:

- **Cached reads (exactly these):** C1 a space's feed page incl. cursor pages (`comm:` keys, 2 s), C2 a thread's
  comment tree incl. B's paged variant (`comments:`/`cmpage:`, 2 s), C3 a search result page (`sp:` keys incl. the
  viewer id, 3 s). B's home-feed cache (`vis:`) is off the measured path and may stay. The markdown render memo
  (key = source text) is allowed in both modes — it is not a data cache.
- **Invalidation, local to the replica, after the write:** thread create/update/delete/pin/move → C1, C2, C3 all ·
  comment create/update → C2 of that thread · comment delete → C1 and C2 all · vote → nothing · comment like → C2
  of that thread · space create/update/delete → everything. No cross-replica invalidation. A result computed
  across an invalidation is not stored (your generation counter).
- **Mechanics:** in-process per replica, about 1 000 entries per cache, singleflight, a cancelled waiter stops
  waiting without cancelling the flight.
- **Switch:** B `GOMX_READ_CACHE` (`1`/`0`), A `ReadCache__Enabled`. Every round runs both sides with the same
  setting: `scripts/ab-round.sh --read-cache on|off` in `forum-dotnet` sets both and verifies both.

## Tasks

1. **Branch.** On `feature/master-thesis` (`c3c0e03` = `thesis-v4-b`), fast-forward to
   `feature/improvements-libraries` (`b7f8fca`). Verify first that it is a fast-forward
   (`git merge-base --is-ancestor origin/feature/master-thesis origin/feature/improvements-libraries`); if it is
   not, stop and ask. Do not bring in `feature/admin-panel`.
2. **`GOMX_READ_CACHE` switch.** Read it with the other config (`app/forum/app.go` → server and feed-service
   config); default `1`. With `0`, `feed.Service` gets nil `feedCache`, `commentCache`, `commentPageCache` and
   `web.Server` a nil `searchCache`. Check that every call site is nil-safe (it looks so today:
   `(*Cache).GetOrCompute` on a nil receiver computes directly, `InvalidateCache`/`invalidateComments` and
   `searchPostsCached` check for nil) and add a test proving that with `0` every read hits the store and nothing
   is stored. Log the effective setting once at startup.
3. **Contract check, no behaviour change.** Confirm in code that the TTLs, keys and invalidation rules above are
   exactly what `b7f8fca` does. If any row differs, do not "fix" the code — report the difference so the contract
   can be corrected.
4. **Metric.** `gomx_read_cache_lookups_total{cache="feed|comments|search",result="hit|miss|bypass"}` next to the
   existing forum metrics, so both sides can report hit ratios.
5. **Bench overlay** (`deploy/k8s/bench/`):
   - add `GOMX_READ_CACHE: "0"` to `app-patch.yaml`; the harness overrides it per round;
   - remove the `nginx.ingress.kubernetes.io/enable-quic` annotation from `ingress-patch.yaml` (A's ingress has
     none; k6 uses TCP, so it only creates an asymmetry for browser runs);
   - fix the PBKDF2 comment: A hashes with **Argon2id**, not ASP.NET Identity's PBKDF2; 100 000 iterations is just
     B's fixed bench cost, and logins happen outside the measured windows;
   - in `README.md`, list the new variable and record that `TuneRuntime` sets `GOMAXPROCS` to the CPU quota (1)
     and `GOMEMLIMIT` to 90 % of the memory limit (record-only facts for the thesis).
6. **Docs.** In `docs/REVIEW-FIXES.md` §5 add the missing row for the data microcaches ("neutralised per round by
   `GOMX_READ_CACHE`; mirrored on A behind `ReadCache__Enabled` per `READ-CACHE-CONTRACT.md`"). Update
   `docs/BENCHMARK-GUARDRAILS.md`: copy the v5 version from `forum-dotnet/docs/specs/B-BENCHMARK-GUARDRAILS.md`
   if it is already pushed, otherwise change only the measured-version lines to `thesis-v5-b` and say so in your
   summary.
7. **Gates** (guardrails §6): `gofmt -l .`, `templ fmt -fail .`, `go vet ./...`, `go test -race ./...`,
   `golangci-lint run`, `templ generate && git diff --exit-code`, plus the Postgres lane from CI.
8. **Hand-off summary for Hubert:** changes, test results, the staged file list, a commit message, then:
   `git tag -a thesis-v5-b -m "thesis-v5-b: improvements-libraries + GOMX_READ_CACHE switch"` on that commit,
   `git push origin feature/master-thesis thesis-v5-b`, and send Jakub the SHA. After the tag nothing on the
   measured surface changes until the v5 rounds are closed.

## After the tag (Hubert, not the agent)

- Run rounds only from a clean `forum-dotnet` checkout at Jakub's tag `thesis-v5-a` and a clean `gomx` checkout
  at `thesis-v5-b` (both `git status` empty — a dirty tree is recorded as `harness_dirty` / `-dirty`); commit
  results afterwards in a separate commit.
- Jakub's `scripts/gomx-bench-deploy.sh` makes the podman image hand-off opt-in: set `GOMX_IMAGE_FROM_PODMAN=1`
  if your minikube needs it.
- Gates before measuring: `make ab-round ARGS="--plan smoke:1 --label v5-gate --read-cache off"` and the same with
  `--read-cache on`; both sides must be valid.
