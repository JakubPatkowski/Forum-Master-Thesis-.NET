# PROPOZYCJA-UJEDNOLICENIA-A-B — A/B Unification Proposal

> **STATUS: DRAFT — pending Hubert's review and sign-off.**
> Drafted 2026-07-18 (A side, read-only audit of both repos). This is the negotiation
> companion to `docs/specs/forum-spec.md`: the spec says *what both systems must equally do*;
> this document says *who changes what, and what deliberately stays different*. Written in
> English per repo convention (the Polish filename is kept because it is referenced under
> this name across CLAUDE.md and the ADRs). It becomes binding at sign-off below.

Full rationale/evidence: `AB-CURRENT-STATE-AUDIT.md`, `AB-THESIS-GAP-ANALYSIS.md`.
Execution detail: `AB-UNIFICATION-MASTER-PLAN.md` + per-side backlogs
(`AB-UNIFICATION-PLAN-ARCHITECTURE-A.md`, `AB-UNIFICATION-PLAN-ARCHITECTURE-B.md`).

---

## 1. Principles (proposed ground rules)

1. **Level up, don't strip down.** Where a capability gap distorts measurement, the weaker
   side builds it; nobody deletes working features to look comparable.
2. **Pin behaviour, not technique.** The spec constrains user-observable outcomes; the
   differing techniques ARE the thesis.
3. **Same instruments, same day.** Every cross-system number comes from one tool version run
   against both systems in one session.
4. **Paradigm differences are findings.** SSR-gets-SEO-free, monolith-goes-offline-on-
   desktop, SPA-needs-token-auth — reported, not "fixed".

## 2. What A changes (Jakub's obligations)

| # | Change | Master-plan ID |
|---|---|---|
| A.1 | Tauri v2 desktop shell for the SPA + mobile feasibility doc | A-1 |
| A.2 | PostgreSQL 17 → 18 everywhere | A-4 |
| A.3 | Lighthouse harness + RUM Web Vitals beacon (mirroring gomx's) | A-2, A-3 |
| A.4 | trivy + SonarQube configs for paired scans | A-6, A-7 |
| A.5 | Frontend + image build added to CI | A-5 |
| A.6 | Dev-loop benchmark harness compatible with `reload-bench` JSON | A-9 |
| A.7 | Artifact-size measurement script | A-8 |
| A.8 | k6 journey alignment to §9.3 of the spec | A-11 |
| A.9 | Benchmark seed extended with social rows (friendships/DMs per spec §7) | (new, from OQ-7) |

## 3. What B is asked to change (proposals for Hubert)

| # | Proposal | Master-plan ID |
|---|---|---|
| B.1 | Deterministic benchmark-scale seed (spec §7 volumes) | B-1 |
| B.2 | k6 profiles matching the joint scenario contract (spec §9) | B-2 |
| B.3 | CI Postgres service 16-alpine → 18-alpine | B-3 |
| B.4 | Documented benchmark-mode limiter values, recorded per run | B-4 |
| B.5 | Run-archival metadata mirroring `thesis/results/` layout | B-6 |
| B.6 | Dev-loop numbers via the shared protocol (harness already exists) | B-7 |
| B.7 | (optional, measure-first) keyset feed pagination if stress shows offset cost | B-5 |

## 4. Joint decisions to lock at sign-off

1. Spec §5 journey list + §9.3 mix/weights.
2. Dataset volumes incl. the new social rows (spec §7, OQ-7).
3. Environment budget + HPA stance (OQ-5/OQ-6).
4. Repetition count N for hypothesis-tested tables (with supervisor).
5. Scanner versions + scan day (trivy, SonarQube).
6. The seven open questions in spec §10.
7. CI comparability stance: descriptive-only for cloud pipelines; local dev-loop timings are
   the tested metric.

## 5. Explicitly staying different (agreed asymmetries)

Auth model (JWT vs cookie sessions) · password KDF (excluded from hot paths) · ID format
(ULID vs bigint) · pagination technique (subject to B.7) · messaging backbone
(RabbitMQ+outbox vs Redis pub/sub) · upload transport (presigned vs server ingest) ·
comment-deletion semantics (OQ-1) · feature breadth listed in spec §4 (tags/groups/blocks/
ACL/audit on A; voice/OAuth/i18n/SEO/reviews on B) · desktop capability (offline monolith vs
connected thin client) · test-culture shape (backend-integration-heavy vs browser-e2e-heavy).

## 6. Timeline proposal

Week 1: review+sign this doc + spec; A.2/A.4/A.5 land. Week 2: A.1/A.3 and B.1/B.2.
Week 3: A.6–A.9, B.4–B.6, joint dry-run (smoke+demo on both). Then measured runs
(interleaved, same day) + paper sections from `THESIS-REVISED-SECTIONS-DRAFT.md`.

## Sign-off

| Author | Date | Decision |
|---|---|---|
| Jakub Patkowski (A) | — | — |
| Hubert Ożarowski (B) | — | — |
