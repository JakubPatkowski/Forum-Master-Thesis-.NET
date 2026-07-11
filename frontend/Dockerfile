# syntax=docker/dockerfile:1
# Build from the REPO ROOT (same build-context convention as the backend image):
#   docker build -f frontend/Dockerfile -t forum-dotnet-web:<tag> .
# (or just `make images` / scripts/build-images.sh, which builds both images.)
# Context filtering lives in frontend/Dockerfile.dockerignore — BuildKit prefers the
# Dockerfile-adjacent `<name>.dockerignore` over the context root's .dockerignore, which is
# what makes a frontend-specific ignore file work with a repo-root context.
FROM node:22-alpine AS build
WORKDIR /app
COPY frontend/package.json frontend/package-lock.json ./
# Cache mount keeps the npm cache across builds — a lockfile change re-downloads only deltas.
RUN --mount=type=cache,target=/root/.npm npm ci
COPY frontend/ .
# NEXT_PUBLIC_* values are inlined into the JS bundles at BUILD time — the image is
# environment-specific by design (documented deviation: a runtime-config indirection is
# over-engineering for a thesis cluster with exactly one environment).
#
# !! NEXT_PUBLIC_API_URL must be the API ORIGIN ONLY: scheme://host[:port], NO path.
# Every call site already passes /api/...-prefixed paths (src/lib/api/http.ts does
# `${apiUrl}${path}`), so a value ending in /api turns every request into /api/api/...
# and 404s the whole app. This is the #1 way to ship a broken frontend image.
#   correct: http://forum.local          http://localhost:8080
#   broken:  http://forum.local/api      http://localhost:8080/api
# NEXT_PUBLIC_WS_URL is deliberately NOT an ARG: when unset, src/lib/config.ts derives
# ws(s)://<api-origin>/api/realtime/ws from NEXT_PUBLIC_API_URL — one knob, no drift.
ARG NEXT_PUBLIC_API_URL=http://forum.local
ENV NEXT_PUBLIC_API_URL=$NEXT_PUBLIC_API_URL
RUN npm run build

FROM node:22-alpine AS runtime
# The runtime executes exactly `node server.js` — drop the bundled package-manager
# toolchain (npm, corepack, yarn) from the final image: it is dead weight and real CVE
# surface (trivy flags npm's own deps, e.g. sigstore/picomatch, as HIGH).
RUN rm -rf /usr/local/lib/node_modules /usr/local/bin/npm /usr/local/bin/npx \
           /usr/local/bin/corepack /usr/local/bin/yarn /usr/local/bin/yarnpkg /opt/yarn*
WORKDIR /app
ENV NODE_ENV=production PORT=3000 HOSTNAME=0.0.0.0
# Standalone output (next.config.ts `output: "standalone"`): server.js + a pruned
# node_modules subset; static assets are served by the same server from .next/static.
# --chown: the node user must own the tree — Next may create .next/cache at runtime.
COPY --from=build --chown=node:node /app/.next/standalone ./
COPY --from=build --chown=node:node /app/.next/static ./.next/static
# NOTE: no COPY of /app/public — the app has no public/ directory (fonts ship via
# next/font, images come from MinIO presigned URLs). Restore the COPY if one appears.
# node = the base image's built-in non-root user (uid 1000). Alpine has a shell (busybox),
# so unlike the chiseled backend image this one IS debuggable via `docker exec -it ... sh`.
USER node
EXPOSE 3000
CMD ["node", "server.js"]
