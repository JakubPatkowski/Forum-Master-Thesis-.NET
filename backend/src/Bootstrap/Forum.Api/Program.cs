using Forum.Api.Correlation;
using Forum.Api.Extensions;
using Forum.Api.Middleware;
using Forum.Common.Correlation;
using Forum.Common.Modules;
using Forum.Infrastructure;
using Forum.Infrastructure.Startup;
using Forum.Modules.Content;
using Forum.Modules.Engagement;
using Forum.Modules.Files;
using Forum.Modules.Identity;

using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Structured logging: console in dev, Loki sink via configuration in cluster.
builder.Host.UseSerilog(static (context, loggerConfiguration) =>
    loggerConfiguration.ReadFrom.Configuration(context.Configuration));

// The modules that make up this monolith. Explicit list = readable and unit-testable.
IReadOnlyList<IModule> modules =
[
    new IdentityModule(),
    new ContentModule(),
    new FilesModule(),
    new EngagementModule(),
];

builder.Services.AddModules(builder.Configuration, modules);
builder.Services.AddForumInfrastructure(builder.Configuration);   // clock, audit, in-process bus, RabbitMQ, MinIO

builder.Services.AddScoped<ICorrelationContext, CorrelationContext>();
builder.Services.AddForumProblemDetails();                        // RFC 7807 for unhandled exceptions
builder.Services.AddForumCors(builder.Configuration);             // SPA origin allow-list
builder.Services.AddForumRateLimiting();                          // per-IP fixed window
builder.Services.AddForumAuthentication(builder.Configuration);   // JWT bearer + authorization skeleton

builder.AddForumObservability();          // OpenTelemetry traces + metrics (+ Prometheus endpoint)
builder.Services.AddForumHealthChecks();  // /health/live, /health/ready
builder.Services.AddForumOpenApi();       // OpenAPI document + JWT Bearer security scheme

var app = builder.Build();

// Migrations run as a one-shot Kubernetes Job (ADR 0005): `dotnet Forum.Api.dll migrate` applies them and exits.
// No-op until a module registers a DbContext (Phase 1+).
if (args.Contains("migrate"))
{
    await app.RunMigrationsAsync();
    return;
}

app.UseExceptionHandler();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseForumSecurityHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();                       // GET /openapi/v1.json
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Forum API v1"));  // GET /swagger
}

app.UseCors(CorsExtensions.SpaPolicy);
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapForumHealthChecks();
app.MapPrometheusScrapingEndpoint();      // GET /metrics
app.MapModules(modules);                   // each module maps its own endpoints

await app.RunWithStartupTasksAsync();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
