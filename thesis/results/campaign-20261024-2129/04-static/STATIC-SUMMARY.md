# Phase 4.3 — static metrics (H3), 2026-10-25 02:52–03:00 CET, on the measured tags

Tags `thesis-v3-a` (`64cd5e2`) and `thesis-v3-b` (`46b342c`); the working trees were clean (= the tags) when scanned.
Tools: trivy 0.72.0 (`trivy-version.txt`; same flags as B's GitLab job — fs scan, vuln + secret + misconfig, HIGH/CRITICAL,
`--ignore-unfixed`, via `scripts/security-scan.sh` which skips local build artefacts = a fresh clone's surface), cloc 2.10
(`--vcs=git` from inside each repo, exclusion lists of `AB-ALIGNMENT-INSTRUCTIONS-A.md` §5.7), image sizes from minikube's
docker (the deployed images). Machine-readable: `static-summary.json` (with the 2026-10-18 values for comparison).
Previous snapshot: `thesis/results/campaign-20261017-2357/04-static/`.

## Security scan (trivy)

| | A now | A 2026-10-18 | B now | B 2026-10-18 |
|---|---|---|---|---|
| fs: HIGH/CRITICAL vulnerabilities with a fix | **0** | 13 (next ×5, sharp ×3, nanoid ×2, postcss ×2, source-map-js ×1) | 0 | 0 |
| fs: secrets | 0 | 0 | 0 | 0 |
| fs: misconfigurations (HIGH) | 5 (KSV-0014 read-only root FS ×3, KSV-0118 default security context ×2) | 5 | 5 (KSV-0014 ×2, KSV-0118 ×2, DS-0002 image user root ×1) | 5 |
| image scan, HIGH/CRITICAL with a fix | api 0 · web 0 | — | 0 | — |

A's 13 frontend-toolchain CVEs of 2026-10-18 are gone (next 15.5.28 + overrides, 2026-10-24). Scope note: trivy fs sees no
transitive .NET test dependencies (no lock files under Central Package Management), while `dotnet build`'s NuGet audit reports
**NU1903 for `SSH.NET 2024.1.0`** (two HIGH advisories published after 2026-10-18; pulled in by Testcontainers in the 8 test
projects only — not shipped in any image; `../01-functional/A/build.log`). The misconfigurations are the bench/k8s manifests'
known findings (read-only root FS and default security contexts on infrastructure pods; B's ContainerFile.dev runs as root).

## Code size (cloc 2.10, tracked files)

| scope | A files | A code | A 2026-10-18 code | B files | B code | B 2026-10-18 code |
|---|---|---|---|---|---|---|
| whole repository | 1 471 | 192 594 | 130 853 | 775 | 96 044 | 95 512 |
| core (application code only) | 797 | 53 914 | 52 097 | 298 | 30 575 | 30 088 |

Core languages: A C# 23 922, TypeScript 11 382, JSON 10 345, CSS 4 917, Markdown 1 941, HTML 728; B Go 18 885, TypeScript
2 846, templ 2 646, TOML 1 393, SQL 1 163, Markdown 1 147. A's whole-repository growth is committed result archives and
documents (JSON 89 594, SVG 21 509, Markdown 14 740 lines); core grew by 1 817 lines (the 2026-10-24 parity work: excerpt,
ACL migration, social seed, tag race, mobile), B's by 487 (card previews, WS gate, hub, 499, vote/like gate).

## Artifact and image sizes

| artifact | A | B |
|---|---|---|
| container images (deployed) | api 178 863 581 B + web 248 770 086 B | 41 202 352 B |
| first-load web assets (gzip) | JS 195 311 + CSS 16 582 = **211 893 B** (route `/`, `.next/app-build-manifest.json`) | styles.css 31 479 + forum.js 70 639 + bridge.js 21 421 + bridge-alpine.js 21 718 = **145 257 B** |
| server binary | (framework-dependent .NET app in the api image) | `/app/server` 39 964 834 B (13 174 075 B gzip -9) |

(v1: A 179 + 243 MB images, 211 KB gzip assets; B 41 MB, 145 KB — essentially unchanged; A's web image +5 MB after the next
15.5.28 bump.) B's binary moved from `/app/bin/server` to `/app/server` — the first extraction attempt (`artifact-size-B.log`)
failed on the old path and was redone (`artifact-metrics-B.txt`).

## Dependency counts (`dependency-counts.json`)

| | A | B |
|---|---|---|
| backend | 42 centrally pinned NuGet package versions (`Directory.Packages.props`) | 29 direct + 105 indirect Go modules (`go.mod`) |
| frontend | 9 dependencies + 15 devDependencies, 669 packages in `package-lock.json` | 9 + 27 (bun), ≈ 1 320 entries in `bun.lock` |

## Lighthouse on the cluster (benchmark data, through the ingress)

`make lighthouse-cluster ARGS="--target a|b --preset mobile|desktop"` (identical `lighthouserc.json` budgets: 2 pages = the
largest public space's feed + its first thread, 3 runs per page, median; mobile = slow-4G + 4× CPU slowdown), 03:03–03:10 CET
on a quiet host (load 2–5, no other job; both apps 2 replicas, A's limiter back at its configmap values). Reports, logs and
OpenMetrics files: `lighthouse/`; table: `lighthouse/comparison-v3-vs-20261018.md`; reference: the 2026-10-18 cluster run
`thesis/results/lighthouse-cluster-20261018/` (tooling-validation run: A web image `git-57f63d0-dirty`, v1 seed + smoke writes, a Playwright build running on the same host — CPU contention inflated TBT/TTI on both sides).

| preset | page | metric | A v3 | B v3 | A 2026-10-18 | B 2026-10-18 |
|---|---|---|---|---|---|---|
| mobile | feed | perf | 0.72 | 0.89 | 0.56 | 0.89 |
| mobile | feed | LCP ms | 2794 | 2178 | 2834 | 2221 |
| mobile | feed | TBT ms | 698 | 11 | 776 | 34 |
| mobile | feed | CLS | 0.149 | 0.195 | 0.237 | 0.195 |
| mobile | feed | FCP ms | 917 | 1356 | 998 | 1353 |
| mobile | feed | a11y | 0.91 | 0.88 | 0.91 | 0.88 |
| mobile | thread | perf | 0.88 | 0.94 | 0.85 | 0.94 |
| mobile | thread | LCP ms | 2896 | 2017 | 2742 | 2028 |
| mobile | thread | TBT ms | 336 | 7 | 442 | 12 |
| mobile | thread | CLS | 0.001 | 0.139 | 0.000 | 0.139 |
| mobile | thread | FCP ms | 916 | 1203 | 909 | 1203 |
| mobile | thread | a11y | 0.95 | 0.94 | 0.95 | 0.94 |
| desktop | feed | perf | 0.97 | 1.00 | 0.97 | 1.00 |
| desktop | feed | LCP ms | 715 | 484 | 597 | 482 |
| desktop | feed | TBT ms | 97 | 0 | 109 | 0 |
| desktop | feed | CLS | 0.090 | 0.012 | 0.078 | 0.018 |
| desktop | feed | FCP ms | 264 | 323 | 248 | 322 |
| desktop | feed | a11y | 0.95 | 0.89 | 0.95 | 0.89 |
| desktop | thread | perf | 1.00 | 1.00 | 0.99 | 1.00 |
| desktop | thread | LCP ms | 652 | 443 | 651 | 442 |
| desktop | thread | TBT ms | 14 | 0 | 68 | 0 |
| desktop | thread | CLS | 0.001 | 0.001 | 0.001 | 0.001 |
| desktop | thread | FCP ms | 255 | 282 | 248 | 282 |
| desktop | thread | a11y | 1.00 | 0.94 | 1.00 | 0.94 |

Reading: A's mobile feed improved (performance 0.56 → 0.72, TBT 776 → 698 ms, CLS 0.237 → 0.149 — the 2026-10-24 mobile
fixes) but remains behind B (0.89; TBT 11 ms) — the CSR hydration cost; desktop is near-equal (A 0.97 / 1.00, B 1.00 / 1.00).
B's mobile TBT fell 34 → 11 ms (consistent with 300-char card previews instead of whole bodies and with the quieter host; the two effects are not separable). A renders the home page again (the v1 regression is
fixed in the v3 web image — `screenshots/A-home.png`).

## Screenshots (`screenshots/`, chrome-headless-shell, 1366×2200 and 412×915, benchmark data)

A and B: home, feed, thread (the heaviest thread on each side), search `beacon`, profile `bench_user_0034` / `/u/34`, plus
the feed at phone width (`manifest.tsv`). Cosmetic note: an excerpt of a thread whose body is only an inline image shows the
raw token (`photo image:<ulid>`, A `thread_excerpt()`), visible on `A-home.png` (thread created by the SQL-count check).
