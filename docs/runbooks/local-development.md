# Local development

```bash
# 1. Infra (Postgres, RabbitMQ, MinIO)
docker compose up -d

# 2. Backend
cd backend
dotnet restore Forum.slnx
dotnet build Forum.slnx
dotnet run --project src/Forum.Host           # https://localhost:xxxx/swagger

# 3. Tests
dotnet test Forum.slnx                          # unit + architecture + integration (Testcontainers)

# 4. Frontend
cd ../frontend && pnpm install && pnpm dev
```

Notes: secrets via `dotnet user-secrets` (never appsettings). Integration tests need Docker (Testcontainers).

# 5. Measurement harnesses (A/B thesis parity — forum-spec §9.5)

All produce artifacts under gitignored `.bench/`, schema-matched to gomx's equivalents:

```bash
make reload-bench                  # dev-loop timings → .bench/reload-bench.json (BENCH_RUNS=10; pair with gomx `task reload-bench`)
scripts/lighthouse.sh              # CWV lab audit (feed + thread) → .bench/lighthouse/ + lighthouse-metrics.txt
scripts/lighthouse.sh --bootstrap-chrome   # one-time, no sudo: portable headless Chrome into .bench/chrome/
make security-scan                 # trivy fs, B-parity flags → trivy-report.json  (paired: scripts/security-scan.sh ../gomx)
make sonar                         # SonarQube C#+TS analysis (needs SONAR_HOST_URL+SONAR_TOKEN; server = gomx scripts/sonarqube-local.sh)
scripts/artifact-size.sh           # first-load JS/CSS + image sizes → .bench/artifact-metrics.txt (installer row: run on Windows)
```

Lighthouse needs the full local stack: seeded API (`make infra-up && make api ARGS=--migrate && make seed`, then `make api`)
— it builds and serves a production frontend bundle itself. RUM field data (`forum_web_vitals_*` on `/metrics`) arrives
automatically from production SPA builds via `POST /api/telemetry/vitals`.
