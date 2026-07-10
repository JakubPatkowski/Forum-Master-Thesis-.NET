# Observability contract (Phase 9a → Phase 10c)

**Status:** authoritative as of Phase 9a (2026-07-10). The names and keys below are **verified by tests**
(`SerilogJsonShapeTests`, `ExceptionPipelineTests`, `ObservabilityFlowTests`) — Phase 10c dashboards, Alloy
config and alert rules must use exactly these, not what older plan prose assumes.

## 1. Structured log shape (stdout, Production)

`appsettings.Production.json` uses **`RenderedCompactJsonFormatter`** (deviation from the plan's
`CompactJsonFormatter`: the Grafana "recent errors" table needs the *rendered* `@m` message, not a template
with `{Placeholders}`). One JSON object per line, keys:

| Key | Meaning | Notes |
|---|---|---|
| `@t` | timestamp (ISO-8601) | always |
| `@m` | rendered message | always |
| `@i` | event id (template hash) | always |
| `@l` | level (`"Debug"`, `"Warning"`, `"Error"`, `"Fatal"`) | **absent on Information lines** (CLEF convention) |
| `@x` | exception + stack trace | only when an exception was logged |
| `@tr` / `@sp` | trace id / span id from `Activity.Current` | present whenever OTel has an active request span |
| `CorrelationId` | the request/message correlation id | pushed by middleware; attached **explicitly** on the unhandled-exception path |
| `SourceContext`, `RequestPath`, … | ordinary Serilog properties | |

> ⚠ **Phase 10c correction:** the plan's Loki derived-field regex `'"TraceId":"(\w+)"'` will never match —
> compact JSON emits **`"@tr"`**, not `"TraceId"`. Use: `matcherRegex: '"@tr":"(\w+)"'`.

After Loki's `| json` parser, `@`-keys are sanitized to `_`-prefixed labels: `@l` → `_l`, `@m` → `_m`,
`@x` → `_x`, `@tr` → `_tr`.

### "Recent errors" table panel (Loki)

```logql
{namespace="forum-dotnet", app="backend"} |= `"@l":"Error"` | json | line_format "{{._m}}"
```

(The literal line filter is cheap and safe because `@l` is only present on non-Information lines; add
`|= `"@l":"Fatal"`` as a second query or use `| json | _l=~"Error|Fatal"` if Fatal should be included.)

### Correlation search (Loki)

```logql
{namespace="forum-dotnet", app="backend"} | json | CorrelationId="<id-from-X-Correlation-ID>"
```

Every ProblemDetails response body now carries `correlationId` (and the framework's `traceId`), and the
`X-Correlation-ID` response header survives the exception-handler path — so the id a user reports is always
searchable, including for 500s.

## 2. Prometheus metric names (exported; scrape `/metrics`)

Domain Meter `Forum` (`Forum.Common.Telemetry.ForumMetrics`), plan names plus the Phase 9a additions:

| Prometheus series | Tags | Meaning |
|---|---|---|
| `forum_auth_attempts_total` | `outcome` = `success` \| `invalid_credentials` \| `blocked` | login attempts |
| `forum_threads_created_total` | — | successful thread creates |
| `forum_comments_created_total` | — | successful comment creates |
| `forum_reactions_total` | `action` = `add` \| `remove` | actual toggles (idempotent no-ops excluded) |
| `forum_outbox_published_total` | `module` | broker-confirmed relay publishes |
| `forum_outbox_publish_failures_total` | `module` | failed relay passes |
| `forum_outbox_lag_seconds_bucket/_sum/_count` | `module` | OccurredOn → broker-confirm latency |
| `forum_messaging_consumed_total` | `module`, `outcome` = `ok` \| `retry` \| `poison` \| `duplicate` | consumer host outcomes |
| `forum_ws_connections` | — | live sockets (UpDownCounter) |
| `forum_ws_subscriptions` | — | live view subscriptions (UpDownCounter) |
| `forum_ws_pushes_total` | — | notifications actually written to a socket |
| **`forum_api_rejections_total`** (new) | `status`, `errorType` = `NotFound`\|`Forbidden`\|`Unauthorized`\|`Conflict`\|`Validation`\|`Failure`\|`BadRequest` | expected Result-pattern rejections; a 401/403 spike is a security signal, a 422 spike a frontend bug |
| **`forum_errors_unhandled_total`** (new) | `category` = `database` \| `timeout` \| `other` (closed set) | unhandled exceptions; client disconnects (OperationCanceledException + aborted request) are **excluded** |
| **`forum_hosted_service_tick_age_seconds`** (new) | `service` = `outbox-relay-<module>` \| `consumer-<module>` \| `orphan-sweep` \| `realtime-feed` | seconds since each background loop last reported alive (series exists from boot — each loop ticks once on start) |

### "Recent errors" rate panel / alert (Prometheus)

```promql
sum by (category) (rate(forum_errors_unhandled_total[5m]))
```

### Security-signal panel

```promql
sum by (errorType) (rate(forum_api_rejections_total{errorType=~"Unauthorized|Forbidden"}[5m]))
```

### Dead-background-loop alert (the relay/consumer/sweeper "silently dead" case)

```promql
max by (service) (forum_hosted_service_tick_age_seconds{service!="orphan-sweep"}) > 120   # these tick ≈ every 1 s–poll interval
max(forum_hosted_service_tick_age_seconds{service="orphan-sweep"}) > 2700                 # sweeper ticks every 15 min — alert at 3×
```

`/health/ready` only proves Postgres/RabbitMQ are reachable — this gauge is what proves the loops are alive.

## 3. Trace pipeline

- ASP.NET Core spans exclude `/health/*` and `/metrics` (same paths excluded from request logging).
- Database spans come from **Npgsql's own ActivitySource** (`AddNpgsql()`), covering EF Core writes *and* raw
  ADO reads with one span per command. The plan's extra `OpenTelemetry.Instrumentation.EntityFrameworkCore`
  was deliberately not wired — it would double-span every EF command (EF instrumentation + Npgsql both wrap
  the same `NpgsqlCommand`) while covering less.
- OTLP endpoint: `Otlp:Endpoint` config key (falls back to the SDK's `OTEL_EXPORTER_OTLP_*` env vars).
- Resource attributes: `service.name` = `Forum.Api`, `deployment.environment` = ASP.NET environment name.
