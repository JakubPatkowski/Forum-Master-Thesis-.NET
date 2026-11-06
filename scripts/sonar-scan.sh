#!/usr/bin/env bash
# SonarQube analysis of forum-dotnet (C# backend + TypeScript frontend) — the A-side half
# of the paired A/B complexity measurement (J-5 / H3, forum-spec §9.5).
#
# WHY THIS SHAPE: C# analysis requires the MSBuild-integrated SonarScanner for .NET
# (dotnet-sonarscanner, pinned in backend/dotnet-tools.json) — the plain sonar-scanner CLI
# does not run the C# analyzer, and it IGNORES sonar-project.properties, so every setting
# lives here as an explicit /d: flag. The scanner's scanAll (default on) picks up the
# non-MSBuild files — the TypeScript in frontend/src — so ONE run covers both languages,
# same as B's single "gomx" project covers Go + TS.
#
# SERVER (scan-day protocol): one shared SonarQube server for BOTH repos so the rule/engine
# version is identical. B already ships a local-server helper — use it for both scans:
#   cd ../gomx && scripts/sonarqube-local.sh start && scripts/sonarqube-local.sh token
#   export SONAR_HOST_URL=http://localhost:9000 SONAR_TOKEN=<token>
#   scripts/sonar-scan.sh                      # (this repo)
#   cd ../gomx && task sonar                   # (B, same server, same day)
# Record the SonarQube server + scanner versions in the results table (forum-spec §9.5).
#
# EXCLUSIONS mirror B's philosophy (generated / vendored / synthetic code is not
# human-maintained source): EF migrations (already generated_code=true in .editorconfig),
# frontend build output, design mockups, the Tauri shell (B excludes src-tauri too), the
# k6 load generator (B excludes loadtest/), and one-off ops scripts (B ignores scripts/**).
set -euo pipefail
source "$(dirname "${BASH_SOURCE[0]}")/lib.sh"

[[ -n "${SONAR_HOST_URL:-}" && -n "${SONAR_TOKEN:-}" ]] \
  || die "set SONAR_HOST_URL and SONAR_TOKEN first (see the header for the scan-day flow)."

# WSL trap: when the Windows TEMP/LOCALAPPDATA path leaks into the environment,
# chrome-launcher hands Chrome a Windows user-data-dir and Linux Chrome creates it as a
# LITERAL relative directory 'C:\Users\...\lighthouse.NNNNNNNN' in the repo root (see the
# guard in scripts/lighthouse.sh). The SonarJS analyzer normalizes those backslashes to
# '/', then dies with ENOENT on a path that cannot exist — but only AFTER the full C#
# build, minutes in. Fail fast instead.
if compgen -G "$REPO_ROOT/C:*" >/dev/null; then
  die "literal 'C:\\...' dirs in the repo root (lighthouse/WSL leak) will crash the SonarJS analyzer — remove them first: rm -rf \"$REPO_ROOT\"/C:*"
fi

cd "$REPO_ROOT/backend"
step "restoring pinned dotnet tools (dotnet-sonarscanner)"
dotnet tool restore >/dev/null

step "sonar begin (project forum-dotnet)"
dotnet dotnet-sonarscanner begin \
  /k:"forum-dotnet" \
  /n:"forum-dotnet" \
  /v:"$(git -C "$REPO_ROOT" rev-parse --short HEAD 2>/dev/null || echo local)" \
  /d:sonar.host.url="$SONAR_HOST_URL" \
  /d:sonar.token="$SONAR_TOKEN" \
  /d:sonar.projectBaseDir="$REPO_ROOT" \
  /d:sonar.exclusions="**/Migrations/**,**/node_modules/**,frontend/.next/**,frontend/out/**,frontend/design-reference/**,frontend/src-tauri/**,load/**,k8s/monitoring/grafana-dashboards/**,**/*.min.js,frontend/public/**" \
  /d:sonar.coverage.exclusions="**" \
  /d:sonar.test.inclusions="**/*.test.ts,**/*.test.tsx" \
  /d:sonar.issue.ignore.multicriteria="scripts1" \
  /d:sonar.issue.ignore.multicriteria.scripts1.ruleKey="*" \
  /d:sonar.issue.ignore.multicriteria.scripts1.resourceKey="scripts/**"

step "dotnet build (analyzed build)"
dotnet build Forum.slnx --no-incremental

step "sonar end (uploads analysis)"
dotnet dotnet-sonarscanner end /d:sonar.token="$SONAR_TOKEN"

ok "analysis uploaded — complexity metrics: $SONAR_HOST_URL/component_measures?id=forum-dotnet&metric=complexity"
