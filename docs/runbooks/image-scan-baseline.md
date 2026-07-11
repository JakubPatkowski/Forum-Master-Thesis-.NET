# Image vulnerability-scan baseline (Phase 10a)

One-time baseline required by the Phase 10a Definition of Done. Re-run on demand with
`make scan` (see [docker-images.md](docker-images.md)); this file is NOT regenerated
automatically.

- **Date:** 2026-07-11
- **Scanner:** Trivy 0.72.0, `--severity HIGH,CRITICAL --ignore-unfixed --exit-code 1`
- **Images:**
  - `forum-dotnet-api` — id `sha256:9f7636ae4397…` (runtime `aspnet:10.0-noble-chiseled-extra`, .NET 10.0.9)
  - `forum-dotnet-web` — id `sha256:d1256b155162…` (runtime `node:22-alpine`, Alpine 3.24.1)

## Result: 0 findings (both images, all targets)

| Image | Target | Type | HIGH/CRITICAL (fixed-only) |
|---|---|---|---|
| forum-dotnet-api | OS packages (ubuntu 24.04, chiseled) | ubuntu | 0 |
| forum-dotnet-api | `app/Forum.Api.deps.json` | dotnet-core | 0 |
| forum-dotnet-api | ASP.NET / .NET runtime deps.json | dotnet-core | 0 |
| forum-dotnet-web | OS packages (alpine 3.24.1) | alpine | 0 |
| forum-dotnet-web | `app/**/package.json` (standalone node_modules) | node-pkg | 0 |

## Findings remediated to reach this baseline

The first scan of the Phase 10a images reported three HIGHs; all were fixed in this phase
rather than waived:

1. **`Microsoft.OpenApi` 2.3.0 — CVE-2026-49451 (HIGH)** in the backend app layer,
   transitive of `Microsoft.AspNetCore.OpenApi` 10.0.1 (also the source of the `NU1903`
   build warning). Fixed via CPM transitive pinning to **2.7.5** in
   `backend/Directory.Packages.props` (the repo has
   `CentralPackageTransitivePinningEnabled=true`, so a `PackageVersion` entry suffices).
   Drop the pin when the parent package catches up. Full test suite green after the bump.
2. **`sigstore` 3.1.0 — CVE-2026-48815 (HIGH)** and **`picomatch` 4.0.3 — CVE-2026-33671
   (HIGH)** in the frontend image — both live under
   `/usr/local/lib/node_modules/npm/…`, i.e. the npm CLI bundled in `node:22-alpine`,
   which the runtime (exactly `node server.js`) never executes. Fixed by deleting the
   package-manager toolchain (npm, npx, corepack, yarn) from the runtime stage in
   `frontend/Dockerfile`. Note: the image SIZE does not shrink (the files are whiteout-ed
   base-layer content), but the merged filesystem — what an attacker and the scanner see —
   no longer contains them.

Accepted, not remediated: `NU1902` *moderate* audit warnings for OpenTelemetry 1.10.0
packages (below this scan's HIGH/CRITICAL gate; revisit on the next OTel bump).
