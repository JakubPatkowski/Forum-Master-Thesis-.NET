# Phase 4 — static metrics (H3), 2026-10-18 03:45–04:03, on the versions measured in the round

Tools: Version: 0.72.0 (same flags as B's CI job: fs scan, vuln+secret+misconfig, HIGH/CRITICAL, --ignore-unfixed), cloc 2.06 (`--vcs=git` from inside each repo, same exclusion lists as the 2026-10-17 snapshot), Lighthouse CI (identical `lighthouserc.json` budgets on both: 3 runs per page, default throttling, portable chrome-headless-shell). Previous snapshot: `thesis/results/static/2026-10-17/`. Raw files next to this summary; machine-readable: `static-summary.json`.

## Security scan (trivy)

| | A now | A 2026-10-17 | B now | B 2026-10-17 |
|---|---|---|---|---|
| HIGH/CRITICAL vulnerabilities with a fix (dependency CVEs) | 13 (CRITICAL 2, HIGH 11) | 13 (CRITICAL 2, HIGH 11) | 0 | 0 |
| distinct vulnerable packages | 10 | 10 | 0 | 0 |
| secrets | 0 | 0 | 0 | 0 |
| misconfigurations (HIGH/CRITICAL, k8s/Dockerfile) | 5 | 7 | 5 | 1 |

Vulnerable packages A: next@15.5.20 (fix 15.5.21, 16.2.11) x3; next@15.5.20 (fix 15.5.24, 16.3.3) x2; nanoid@3.3.15 (fix 3.3.18, 5.1.6) x1; nanoid@3.3.15 (fix 3.3.16, 5.1.16) x1; postcss@8.4.31 (fix 8.5.12) x1; postcss@8.4.31 (fix 8.5.18) x1; sharp@0.34.5 (fix 0.35.0) x1; sharp@0.34.5 (fix 0.35.4) x1; sharp@0.34.5 (fix 0.35.5) x1; source-map-js@1.2.1 (fix 1.2.2) x1

Vulnerable packages B: none

Misconfigurations A: HIGH KSV-0014 Root file system is not read-only x3; HIGH KSV-0118 Default security context configured x2

Misconfigurations B: HIGH KSV-0014 Root file system is not read-only x2; HIGH KSV-0118 Default security context configured x2; HIGH DS-0002 Image user should not be 'root' x1

## Code size (cloc)

| scope | A files | A code | A 2026-10-17 code | B files | B code | B 2026-10-17 code |
|---|---|---|---|---|---|---|
| whole | 1109 | 130853 | 85389 | 772 | 95512 | 94864 |
| core | 789 | 52097 | 51745 | 295 | 30088 | 28755 |

Core scope = application code only (A: excludes EF migrations, design reference, Tauri, build output, thesis/docs/k8s/load/scripts; B: excludes sqlc/templ generated code, templbook/templdesign, static bundles, Tauri, loadtest, bruno, deploy/scripts/docs). Languages (core, code lines): A C# 23166, TypeScript 10652, JSON 10305, CSS 4666, Markdown 1907, HTML 728; B Go 18403, TypeScript 2846, Templ 2641, TOML 1393, SQL 1163, Markdown 1147. A's whole-repo growth since 2026-10-17 (85 k → 131 k) is the round/campaign archives and documents (JSON/Markdown); core grew by 352 lines (A) and 1 333 lines (B: the alignment commits).

## Artifact and image sizes

| artifact | bytes raw | bytes gzip |
|---|---|---|
| A first_load_js | 644653 | 194901 |
| A first_load_css | 69040 | 15801 |
| A first-load web assets total (gzip) | – | 210702 |
| B server | 39948450 | 13167067 |
| B styles.css | 211225 | 31472 |
| B bridge-alpine.js | 62806 | 21718 |
| B bridge.js | 71629 | 21421 |
| B forum.js | 209100 | 70639 |
| B web assets total css+js (gzip) | – | 145250 |

| container image (minikube daemon) | bytes now | bytes 2026-10-17 |
|---|---|---|
| gomx:bench | 41188802 | 41172418 |
| forum-dotnet-api:git-57f63d0-dirty | 178769346 | 178743032 |
| forum-dotnet-web:git-57f63d0-dirty | 243475415 | 243475415 |

B's `server` is the whole application (static Go binary with the bundles embedded; the image is distroless-like, runs as root → trivy DS-0002); A ships two images (API + SPA shell server). A's first-load assets are the JS/CSS the browser downloads for `/` on first paint; B's css+js are served by the binary.

## Dependency surface

| | A | B |
|---|---|---|
| backend packages | 42 NuGet versions pinned centrally (24 distinct referenced in src) | 29 direct + 105 indirect Go modules (227 modules in go.sum) |
| frontend / tooling packages | npm 9 deps + 14 devDeps (665 packages in package-lock) | bun 9 deps + 27 devDeps (1320 packages in bun.lock) |

## Lighthouse (lab, local, demo-scale data — NOT the cluster/benchmark dataset)

A: production `next build` (working tree incl. the 03:47 home-feed fix) served by `next start :3000` against the working-tree API (`scripts/dev-api.sh --migrate`, Development seed incl. the private `private-club` category) on :8080 — cross-origin → CORS preflights (D-18). B: `gomx:bench` image in docker with its in-memory demo seed on :8080. Page 2 = one thread page per side. Budgets (report-only, identical): performance ≥ 0.9, a11y/bp/seo = 1, LCP ≤ 2500 ms, CLS ≤ 0.1, TBT ≤ 200 ms, FCP ≤ 1800 ms.

| page | metric | A now | A attempt 1 (old compose API) | A 2026-10-17 | B now | B 2026-10-17 |
|---|---|---|---|---|---|---|
| home / | score performance | 0.84 | 0.75 | 0.8 | 0.92 | 0.91 |
| home / | score accessibility | 0.95 | 0.95 | 0.95 | 0.87 | 0.91 |
| home / | score best-practices | 0.96 | 0.96 | 0.96 | 1.0 | 1.0 |
| home / | score seo | 1.0 | 1.0 | 1.0 | 1.0 | 1.0 |
| home / | lcp_ms | 4135 | 4361 | 4311 | 1891 | 2230 |
| home / | fcp_ms | 1069 | 1072 | 1069 | 938 | 1055 |
| home / | tbt_ms | 172 | 401 | 272 | 26 | 0 |
| home / | cls | 0.01 | 0.01 | 0.01 | 0.177 | 0.177 |
| home / | tti_ms | 4135 | 4361 | 4311 | 2243 | 2238 |
| thread page | score performance | 0.81 | 0.77 | 0.81 | 0.99 | 0.99 |
| thread page | score accessibility | 0.95 | 0.95 | 0.95 | 0.9 | 0.94 |
| thread page | score best-practices | 0.96 | 0.96 | 0.96 | 1.0 | 1.0 |
| thread page | score seo | 1.0 | 1.0 | 1.0 | 1.0 | 1.0 |
| thread page | lcp_ms | 4007 | 3975 | 3917 | 2089 | 2085 |
| thread page | fcp_ms | 1070 | 1068 | 1068 | 1056 | 1058 |
| thread page | tbt_ms | 279 | 417 | 317 | 5 | 2 |
| thread page | cls | 0.0 | 0.0 | 0.0 | 0.041 | 0.041 |
| thread page | tti_ms | 4007 | 3975 | 3917 | 2097 | 2092 |

Provenance note: the first A audit (03:47, `lighthouse-A-attempt1-old-compose-api/`) unknowingly ran against the compose `api` container (image built 2026-07-18, pre-A-12) because `docker compose start` had started it on :8080 and the working-tree API failed to bind; it is kept for transparency only. The recorded result (`lighthouse-A/`, `lighthouse-metrics-A.txt`, 04:01–04:03) ran against the working-tree API (verified: anonymous private feed → 404) with the home-feed fix in the bundle. Lab Lighthouse on this shared host is noisy (A home 0.75 → 0.84 between the two attempts on the same bundle, mostly TBT); treat differences < 0.1 as noise.

## Not run

- dev-loop benchmark (A `scripts/dev-loop-bench.sh`, B `task reload-bench`): B's half needs the Go/bun/task toolchain on the host (not installed) — an A-only number would not be a comparison; the 2026-10-17 A result stays the reference.
- Lighthouse against the cluster on the Benchmark dataset: v2 item (AB-START-HERE §8).
