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
