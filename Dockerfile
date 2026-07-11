# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore layer (cache-friendly): build config + all projects, then restore the host graph.
# The BuildKit cache mount persists the NuGet package cache across builds, so even a
# csproj/CPM edit (which invalidates these layers) re-downloads next to nothing.
# .editorconfig must land at the same ancestor level as in the repo: it tunes analyzer
# severities (e.g. CA1711/CA1716 -> none) that otherwise FAIL the Release publish.
COPY .editorconfig ./
COPY backend/global.json backend/Directory.Build.props backend/Directory.Packages.props ./backend/
COPY backend/src ./backend/src
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore backend/src/Bootstrap/Forum.Api/Forum.Api.csproj
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish backend/src/Bootstrap/Forum.Api/Forum.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# Runtime: Ubuntu "chiseled" = Canonical's distroless — no shell, no package manager, no
# coreutils; drastically smaller attack/CVE surface. The -extra variant is REQUIRED, not
# plain chiseled: it adds ICU + tzdata, and the app relies on culture-aware string
# comparisons (PostgreSQL citext interplay) — plain chiseled silently falls back to
# invariant globalization and subtly changes string behavior. Do not "optimize" this away.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app .
# The image ships a built-in non-root user and already runs as it by default (verified on
# this tag: Config.User=1654, /etc/passwd -> app:x:1654:1654, shell /bin/false). There is
# no useradd here — chiseled has no shell to run it. `USER app` is redundant-but-explicit
# self-documentation. k8s note (Phase 10b): keep runAsNonRoot: true and do NOT pin
# runAsUser — the image's own default user applies.
USER app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0
# No shell in this image: `docker exec`/`kubectl exec` cannot open a session, and compose
# healthchecks must be TCP-based or absent (never curl/CMD-SHELL). Debugging escape
# hatches (full workflow in docs/runbooks/docker-images.md):
#   docker: inspect via `docker cp <ctr>:/app/... .` + `docker logs`; or temporarily point
#           this stage at mcr.microsoft.com/dotnet/aspnet:10.0 (one line) for a local shell.
#   k8s:    kubectl debug -it <pod> --image=busybox --target=backend  (ephemeral container).
ENTRYPOINT ["dotnet", "Forum.Api.dll"]
