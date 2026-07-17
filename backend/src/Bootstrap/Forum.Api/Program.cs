using Forum.Api.Correlation;
using Forum.Api.DevTools;
using Forum.Api.Extensions;
using Forum.Api.Middleware;
using Forum.Common.Correlation;
using Forum.Common.Modules;
using Forum.Infrastructure;
using Forum.Infrastructure.Seeding;
using Forum.Infrastructure.Startup;
using Forum.Modules.Content;
using Forum.Modules.Engagement;
using Forum.Modules.Files;
using Forum.Modules.Identity;
using Forum.Modules.Social;

using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(args);

// Structured logging: human-readable console in dev; compact JSON to stdout in Production
// (appsettings.Production.json), shipped to Loki by Alloy — the app never buffers or pushes logs itself.
builder.Host.UseSerilog(static (context, loggerConfiguration) =>
    loggerConfiguration.ReadFrom.Configuration(context.Configuration));

// The modules that make up this monolith. Explicit list = readable and unit-testable.
IReadOnlyList<IModule> modules =
[
    new IdentityModule(),
    new ContentModule(),
    new FilesModule(),
    new EngagementModule(),
    new SocialModule(),
];

builder.Services.AddModules(builder.Configuration, modules);
builder.Services.AddForumInfrastructure(builder.Configuration);   // clock, audit, in-process bus, RabbitMQ, MinIO

builder.Services.AddScoped<ICorrelationContext, CorrelationContext>();
builder.Services.AddForumProblemDetails();                        // RFC 7807 for unhandled exceptions
builder.Services.AddForumForwardedHeaders(builder.Configuration); // X-Forwarded-* trust (ingress in k8s only)
builder.Services.AddForumCors(builder.Configuration);             // SPA origin allow-list
builder.Services.AddForumRateLimiting(builder.Configuration);     // per-IP fixed window
builder.Services.AddForumAuthentication(builder.Configuration, builder.Environment); // JWT bearer + authz; fails fast in Production without a real signing key
builder.Services.AddForumRealtime(builder.Configuration);         // WebSocket hub: tickets + change-feed fan-out

builder.AddForumObservability();          // OpenTelemetry traces + metrics (+ Prometheus endpoint)
builder.Services.AddForumHealthChecks();  // /health/live, /health/ready
builder.Services.AddForumOpenApi();       // OpenAPI document + JWT Bearer security scheme

// Graceful shutdown (G17): drain in-flight work within 25 s — k8s gives 40 s (terminationGracePeriodSeconds)
// minus a 5 s preStop sleep, so the host always finishes before the SIGKILL.
builder.Services.Configure<HostOptions>(static options => options.ShutdownTimeout = TimeSpan.FromSeconds(25));

var app = builder.Build();

// Migrations run as a one-shot Kubernetes Job (ADR 0005): `dotnet Forum.Api.dll migrate` applies them and exits.
// No-op until a module registers a DbContext (Phase 1+).
if (args.Contains("migrate"))
{
    await app.RunMigrationsAsync();
    return;
}

// Deterministic dataset seeding runs the same way (ADR 0005 pattern): `dotnet Forum.Api.dll seed [--benchmark]
// [--force]` populates a migrated database and exits. Never runs on a normal boot. --benchmark selects the larger
// A/B dataset (default: the tiny Development profile); --force TRUNCATEs first so a populated DB can be reset.
if (args.Contains("seed"))
{
    var profile = args.Contains("--benchmark") ? SeedProfile.Benchmark : SeedProfile.Development;
    await app.RunSeedAsync(new SeedConfig(profile, AllowTruncate: args.Contains("--force"), Verbose: args.Contains("--verbose")));
    return;
}

// First, before anything reads RemoteIpAddress (rate limiter, request logs, correlation) — a no-op unless the
// caller is a proxy inside ForwardedHeaders:KnownNetworks.
app.UseForwardedHeaders();

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging(static options =>
    // kubelet probes (2 × every ~10 s per pod) and Prometheus scrapes (15 s) would dominate Loki's ingest for
    // zero diagnostic value — Verbose keeps them out at the default minimum yet available when troubleshooting.
    options.GetLevel = static (httpContext, _, exception) =>
        exception is not null || httpContext.Response.StatusCode >= StatusCodes.Status500InternalServerError
            ? LogEventLevel.Error
            : httpContext.Request.Path.StartsWithSegments("/health")
                || httpContext.Request.Path.StartsWithSegments("/metrics")
                ? LogEventLevel.Verbose
                : LogEventLevel.Information);
app.UseForumSecurityHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();                       // GET /openapi/v1.json
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Forum API v1"));  // GET /swagger
    app.MapDevMonitor();                    // GET /dev/monitor — live bus + WebSocket-hub observability page
}

app.UseCors(CorsExtensions.SpaPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapForumHealthChecks();
app.MapPrometheusScrapingEndpoint();      // GET /metrics
app.MapForumRealtime();                    // POST /api/realtime/ticket + GET /api/realtime/ws
app.MapModules(modules);                   // each module maps its own endpoints

await app.RunWithStartupTasksAsync();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
