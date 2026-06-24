using Forum.Api.Extensions;
using Forum.Common.Modules;
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

builder.AddForumObservability();          // OpenTelemetry traces + metrics (+ Prometheus endpoint)
builder.Services.AddForumHealthChecks();  // /health/live, /health/ready
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseForumSecurityHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapForumHealthChecks();
app.MapPrometheusScrapingEndpoint();      // GET /metrics
app.MapModules(modules);                   // each module maps its own endpoints

await app.RunWithStartupTasksAsync();

// Exposed for WebApplicationFactory in integration tests.
public partial class Program;
