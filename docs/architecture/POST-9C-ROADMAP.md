# Post-9c roadmap — what's next for forum-dotnet

_Written: 2026-07-16, branch `25-feat-phase-9c---k6-load-profiles`, right after Phase 9c went code-complete (still uncommitted). This is a working roadmap, not an ADR and not part of `PHASE-9-10-ENTERPRISE-PLAN.md` — update or retire it once it goes stale. Superseded/updated 2026-07-16 (same day) after the Redis and Social scope were revised — see "Decisions" below._

## Context

Phase 9c (k6 load profiles + benchmark runbook) is code-complete and verified live on the cluster (2026-07-16), but the changes are still uncommitted. This document tracks what's left from `PHASE-9-10-ENTERPRISE-PLAN.md` plus five self-directed initiatives (Redis, the Social module, preparing for the comparison against Architecture B / Hubert's gomx, a security + performance audit, diagrams/SVGs for the README, and general docs/repo polish on GitHub), plus what to delegate to Fable 5 and in what order.

Three parallel research passes (a full read of `PHASE-9-10-ENTERPRISE-PLAN.md`, a search for the shared spec docs referenced for the gomx comparison, and a survey of `README.md`/`thesis/`/`docs/screenshoots/`) produced the evidence this roadmap is based on.

## Decisions

1. **Redis: reversed — now planned, scope-limited, portfolio-driven.** The original §10d verdict ("no Redis") was and remains *performance-correct* — every hot path measured against live Tempo data was 1–2 sub-2ms Npgsql spans, nothing for a cache tier to win. That finding is not being retracted. The decision to add Redis anyway is explicit and orthogonal to that finding: to demonstrate distributed-caching/session/rate-limiting skills for the portfolio. Scope, deliberately **not** full-API response caching:
   - category list cache
   - application configuration cache
   - most-popular-posts cache
   - rate limiting (distributed — replaces the current per-replica `PartitionedRateLimiter`, closing the documented ×replicas multiplier)
   - distributed session cache
   Implementation is delegated to a dedicated Fable 5 session (see Phase 1 below).
2. **Social module: confirmed, scope expanded to include groups.** Hubert (Architecture B) confirmed he's building the same relationship surface on gomx — and he has already added **groups** there, so B-parity now requires groups here too, beyond the original friends+DM-only scope in `REQUIREMENTS-AND-ASSUMPTIONS.md` §1 / `PHASE-9-10-ENTERPRISE-PLAN.md` §10e. Full scope: **friends** (request/accept/remove), **direct messages**, and **groups** (create/invite/join/leave/membership roles) — and everything that needs it must be **pushed live over the existing WebSocket hub**: invitations, messages, membership changes, not just picked up on next poll. Build is split across two separate Fable 5 sessions ("MaxCode"): one scoped to the backend (`Forum.Modules.Social` + realtime wiring), one scoped to the frontend (wiring the existing `/social` mock page to the real endpoints).
3. **`docs/specs/forum-spec.md` and `docs/architecture/PROPOZYCJA-UJEDNOLICENIA-A-B.md` never existed.** Both are cited as the authoritative shared A/B contract by CLAUDE.md and several ADRs, but are confirmed absent from the repo and from git history on every branch. This is real documentation debt. **Writing the actual A/B comparison content is blocked for now** — Hubert hasn't landed his side's changes yet — and will happen once he has (soon, per the user). Some of the surrounding groundwork (this roadmap, the Social scope, the Redis scope) can still move forward in the meantime.
4. **§10d step 6 (the "before/after" benchmark re-run): closed by documentation, not by re-running anything.** The 10d optimizations landed 2026-07-15, *before* 9c's first archived benchmark run (2026-07-16) — so no genuine "before" snapshot exists, and none will be manufactured by reverting config just to produce one. The qualitative record (Tempo span evidence + the SPA-side N+1 fix, both already in the 10d block of CLAUDE.md's Current State) is the accepted record for this item. Nothing here is being rolled back.

## What's actually left from the plan (audit of `PHASE-9-10-ENTERPRISE-PLAN.md`)

- **§10d step 6** — resolved per Decision 4 above: documented, not re-run.
- **§10e (Social)** — scope skeleton in the plan (schema `forum_social`: `friendships`, `direct_messages`, no presence/read-receipts/voice) is now superseded by Decision 2 above (groups added, full realtime requirement). The plan's other two go/no-go conditions (phases 9a–10d archived, ≥2 working weeks left before the thesis freeze) still deserve a conscious sanity check before deep implementation, but the B-parity condition is satisfied.
- **Plan's changelog + §13 (CLAUDE.md sync)** are not filled in for blocks 9b/9c/10b/10c/10d — pure formal debt, quick to close.
- **Explicitly "listed, not built" in the plan:** the CI image-scan (Trivy) step — though `.github/workflows/security.yml` with `dotnet list package --vulnerable` already exists, so this line in the plan may be stale; verify what's actually wired before assuming it's missing. Also: a true DAU metric, and prometheus-adapter (this one deliberately rejected, not a gap).
- **The plan contains nothing** about diagrams/SVGs, README/GitHub polish, or a formal "security/performance audit" as its own phase — these are entirely self-directed initiatives, not in conflict with the plan.
- **The Architecture-B comparison methodology is scattered across the document**, not a single section: same seed volumes as B, same resource limits, never run A and B simultaneously, ≥3 repeats with mean/stddev, same k6 host/profile/think-time, record git SHA + image digest in `meta.json`, Grafana screenshots per run window. `bench-run.sh` only archives to `thesis/results/A/...` — there's no joint script that ingests B's results.

## Roadmap — order of work

### Phase 0 — Housekeeping (fast, unblocks everything else)
1. ~~§10d step 6~~ — done (Decision 4, recorded directly in CLAUDE.md's Current State).
2. Fill in the plan's changelog + CLAUDE.md's "Current state" sync for blocks 9b/9c/10b/10c/10d (the plan's own §13 instruction, quick formal cleanup).
3. Hold off on writing `docs/specs/forum-spec.md` / `PROPOZYCJA-UJEDNOLICENIA-A-B.md` until Hubert's side is ready (Decision 3) — revisit as soon as he lands his changes.

### Phase 1 — Redis (scope-limited, one dedicated Fable 5 session)
- Category list cache, config cache, most-popular-posts cache, distributed rate limiting, distributed session cache — see Decision 1 for the exact boundary (no full-API caching).
- Rate limiting: replacing the per-replica `PartitionedRateLimiter` with a Redis-backed distributed limiter also closes the ×replicas-multiplier note from the original §10d table — worth calling out as a concrete improvement in the writeup, not just a skill demo.
- Session cache: define what "session" means precisely here (the app currently uses stateless JWT access + httpOnly refresh cookie rotation, not server-side session state) before implementation starts, so the Fable 5 session has an unambiguous target.
- k8s: single `redis:7-alpine` Deployment (no HA needed), restricted securityContext, Service `redis:6379`, NetworkPolicy `backend → redis:6379`, `StackExchange.Redis` via Central Package Management — this mirrors the "if reversed" recipe already written into the original §10d analysis, so most of the operational shape is already decided.

### Phase 2 — Social module (biggest remaining chunk of new work, two Fable 5 sessions)
- **Ready-to-paste prompts for both sessions:** `docs/architecture/SOCIAL-MODULE-FABLE5-PROMPTS.md` (written 2026-07-16, grounded in a direct research pass over the ACL/realtime/messaging/Files/frontend code — includes the proposed domain model, the required Bootstrap-realtime and Files-module edits, the presence/Redis-readiness seam, and the presence-vs-REQUIREMENTS-AND-ASSUMPTIONS-§1-scope flag).
- **Backend session:** new `Forum.Modules.Social` project (Domain/Application/Infrastructure/Presentation/Contracts), schema `forum_social` — `friendships`, `direct_messages`, and **groups** (membership + roles + invites). Use cases in the existing house style (Result pattern, CQRS without MediatR, patterns borrowed from Content/Engagement). Integration events + wiring into the WebSocket hub (mirroring the Phase 7 pattern) for every action that needs to be live: friend requests/invitations, messages, group membership changes.
- **Frontend session:** wire the existing `/social` page (currently a zero-fetch UI-only mock with a PREVIEW banner) to the real endpoints, plus whatever realtime subscriptions the backend session exposes.
- Seed/benchmark numbers (friendships, DMs, groups) and k6 traffic-mix additions are blocked on Hubert's final counts (Decision 3) — don't hardcode numbers yet.
- DoD: module boundary tests extended, E2E coverage for friends/DM/groups incl. the realtime push paths, "B parity confirmed in writing" formalized once the shared spec docs exist.

### Phase 3 — Architecture-B comparison prep (blocked, resume once Hubert lands his changes)
- Lock dataset shape/volume with B (A already has: 800 users/12 categories/60 tags/1600 threads/9000 comments/15000 reactions — B needs to match shape, not literal text; new Social counts still TBD per Decision 3).
- Confirm B's resource limits match A's contract (12 GiB RAM / 6 vCPU).
- Schedule: A and B never simultaneously, same k6 host, same profiles (smoke/demo/stress), same think-time.
- Prepare a `thesis/results/B/...` convention mirroring A's structure so both archives line up directly.
- Once both sides are ready: the actual comparative run + the results table/charts for the thesis.

### Phase 4 — Security audit (independent, can run alongside Phase 2)
- Refresh the Trivy baseline scan (`make scan`) and update `docs/runbooks/image-scan-baseline.md`.
- Verify whether `.github/workflows/security.yml` already covers container image scanning or only `dotnet list package --vulnerable` — the plan's "listed, not built" note may be stale.
- Run a structured security code review (the `/security-review` skill) focused on Identity/Authz: JWT, Argon2id, ACL SQL, the WS ticket handshake.
- Consider a light dynamic pass (e.g. an OWASP ZAP baseline scan through ingress) — strengthens the thesis's security chapter, optional.
- Write up findings as a new runbook, following the existing `image-scan-baseline.md` pattern.

### Phase 5 — Performance audit (mostly already done)
- Effectively closed by the §10d Tempo-span audit and the §9c stress-profile knee (documented at 150 VU).
- The only missing piece is Phase 0 item 1, which is now done (documented, not re-run).
- Optional: a deeper `dotnet-trace`/`dotnet-counters` capture at the stress-profile peak (150 VU) for extra depth in the thesis — nice-to-have, not required by the plan.

### Phase 6 — Diagrams/SVGs + README/GitHub polish (independent, can run in parallel)
- Wire the existing `docs/architecture/diagrams/k8s-cluster-architecture.svg` into the README (it exists, just isn't embedded).
- Draw a NEW module-architecture diagram (SVG, same hand-drawn dark theme) — Bootstrap/Shared/Modules + Contracts boundaries.
- Fix the `docs/screenshoots/` typo → `docs/screenshots/` and embed the two existing Grafana HPA screenshots in the README.
- Capture NEW SPA screenshots (feed/thread/realtime/upload) — requires running the frontend locally.
- Wire README badges to real CI status (Build/Tests from GitHub Actions; drop or mark Coverage as untracked).
- Clean up the README: remove the leftover AI-edit meta-note ("could not access c:/Users/..."), update the phase checklist (9c is done, not "planned"), add a short placeholder "vs Architecture B" section (to fill in once Phase 3 produces data).
- General GitHub repo polish: description/topics, verify the LICENSE file, maybe a short CONTRIBUTING.

## Recommended execution order

1. **Phase 0** (housekeeping) — cheap, fast, unblocks everything and closes the documentation-citation debt.
2. **Phase 1 (Redis)** and **Phase 2 (Social)** — both delegated to dedicated Fable 5 sessions (3 sessions total: Redis, Social-backend, Social-frontend); these can run largely in parallel with each other since they touch different modules.
3. **In parallel with 1/2:** Phase 4 (security audit) and Phase 6 (diagrams/README) — independent of Redis/Social, good candidates to run alongside or delegate separately.
4. **Phase 3** (Architecture-B comparison prep) — blocked until Hubert lands his changes; mostly coordination work once unblocked, plus the final joint benchmark run.
5. **Phase 5** (performance audit close-out) — folds into the final run in Phase 3.

## What to delegate to Fable 5

Now that the scope is concrete, the assignment is no longer speculative:
- **One Fable 5 session — Redis** (Phase 1): category list cache, config cache, popular-posts cache, distributed rate limiting, distributed session cache. Scope boundary must be explicit in the session's brief: no full-API caching.
- **One Fable 5 session — Social backend** (Phase 2): `Forum.Modules.Social` (friends + DMs + groups) + WebSocket wiring for everything realtime.
- **One Fable 5 session — Social frontend** (Phase 2): wire `/social` to the real endpoints + the new realtime subscriptions.
- Diagrams/README polish (Phase 6) remains a reasonable Fable 5 candidate too (visual/editorial work, low blast radius), but is not yet assigned to a specific session.

## Verification

This is a planning document, not a code change — nothing to run right now. Once each phase moves into implementation, the standard DoD pattern from `IMPLEMENTATION-PLAN.md`/`PHASE-9-10-ENTERPRISE-PLAN.md` applies: `dotnet build`/`test`/`format --verify-no-changes`, a live smoke test on the cluster where relevant, and (for frontend work) actually exercising it in a browser per CLAUDE.md's rules.
