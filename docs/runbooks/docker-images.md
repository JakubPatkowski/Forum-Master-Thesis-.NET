# Docker images (Phase 10a)

Two application images, both built from the **repo root** (one build-context convention):

| Image | Dockerfile | Base (runtime) | User | Port | Purpose |
|---|---|---|---|---|---|
| `forum-dotnet-api` | `Dockerfile` | `mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra` | `app` (uid 1654, image default) | 8080 | Forum.Api — also runs `migrate` / `seed` as Job args |
| `forum-dotnet-web` | `frontend/Dockerfile` | `node:22-alpine` | `node` (uid 1000) | 3000 | Next.js standalone server (CSR shell only) |

```bash
make images                     # build both, tag = git-<short-sha>[-dirty]
make images ARGS=--no-cache     # cold build (flags pass through to both docker builds)
make scan                       # Trivy HIGH/CRITICAL, --ignore-unfixed (needs trivy)
```

## Tagging

`IMAGE_TAG` defaults to `git-<short-sha>` of `HEAD`, with `-dirty` appended when the working
tree has uncommitted changes (`scripts/lib.sh`). Every benchmark number therefore maps to an
exact, inspectable build — record `docker image inspect --format '{{.Id}}' <image:tag>`
alongside results. `IMAGE_TAG=local` (env or `.env`) restores the historical fixed tag.
The k8s manifests still pin `:local`; `deploy.sh` retags the fresh build onto it — Phase 10b
replaces that with explicit `kubectl set image` pinning for rollout/rollback support.

## Backend: chiseled runtime — what changes for you

The runtime base is Ubuntu **chiseled** (Canonical's distroless): no shell, no package
manager, no coreutils. The `-extra` variant is required — it ships ICU + tzdata; plain
chiseled would drop .NET to invariant globalization and subtly change culture-aware string
comparisons (PostgreSQL citext interplay).

Consequences:

- **`docker exec -it <ctr> sh` does not work.** Neither does `kubectl exec`.
- Compose healthchecks for the `api:` service must be **TCP-based or absent** — never
  `curl`/`CMD-SHELL` (there is nothing to shell out to).
- The image runs as its built-in non-root user `app` (uid 1654) by default. In k8s
  (Phase 10b): keep `runAsNonRoot: true`, drop any `runAsUser` pin.

### Debugging a shell-less container

| Situation | Escape hatch |
|---|---|
| Inspect files in a running/stopped container | `docker cp <ctr>:/app/appsettings.json .` (works without a shell) |
| Logs / crash loops | `docker logs <ctr>` — Serilog writes everything to stdout |
| Probe the app from inside the network | run a sidecar: `docker run --rm --network <net> curlimages/curl -sf http://api:8080/health/live` |
| Need an actual interactive session locally | temporarily change the runtime `FROM` to `mcr.microsoft.com/dotnet/aspnet:10.0` (one line in `Dockerfile`), rebuild — plain aspnet has bash |
| k8s (from Phase 10b on) | `kubectl debug -it <pod> --image=busybox --target=backend` (ephemeral container shares the pod's namespaces) |

## Frontend: build-time API origin (the #1 trap)

`NEXT_PUBLIC_*` is inlined into the JS bundles at **build time** — the image is
environment-specific by design. The build ARG contract:

- `NEXT_PUBLIC_API_URL` = API **origin only**: `scheme://host[:port]`, **never** a `/api`
  path. Call sites already prefix `/api/...`; a value ending in `/api` turns every request
  into `/api/api/...` and 404s the whole app. `scripts/build-images.sh` refuses such values.
- `NEXT_PUBLIC_WS_URL` is deliberately **not** an ARG — the client derives
  `ws(s)://<api-origin>/api/realtime/ws` from the API URL when unset. One knob, no drift.

Full build/smoke-test instructions: `frontend/README.md` → "Docker image".

## Vulnerability scanning

`scripts/scan-image.sh` (or `make scan`) runs Trivy with `--severity HIGH,CRITICAL
--ignore-unfixed --exit-code 1` against both images — non-zero exit on any finding with an
available fix. It is an on-demand step, not CI-wired (the workflow only builds/tests today).
Baseline report: [image-scan-baseline.md](image-scan-baseline.md).
