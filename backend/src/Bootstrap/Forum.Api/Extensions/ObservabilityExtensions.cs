using Forum.Common.Telemetry;

using Npgsql;

using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Forum.Api.Extensions;

public static class ObservabilityExtensions
{
    /// <summary>OpenTelemetry traces + metrics, exported via OTLP (Tempo) and scraped by Prometheus.</summary>
    public static WebApplicationBuilder AddForumObservability(this WebApplicationBuilder builder)
    {
        // OTLP target: config first (compose/k8s set Otlp:Endpoint), the SDK's OTEL_EXPORTER_OTLP_* env vars as
        // the fallback when the key is absent.
        var otlpEndpoint = builder.Configuration["Otlp:Endpoint"];

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(builder.Environment.ApplicationName)
                .AddAttributes([new KeyValuePair<string, object>(
                    "deployment.environment", builder.Environment.EnvironmentName)]))
            .WithTracing(tracing => tracing
                // Probe/scrape traffic (2 probes × N pods every ~10 s + Prometheus every 15 s) would drown Tempo
                // in identical spans; the same paths are excluded from request logging in Program.cs.
                .AddAspNetCoreInstrumentation(options => options.Filter = static context =>
                    !context.Request.Path.StartsWithSegments("/health")
                    && !context.Request.Path.StartsWithSegments("/metrics"))
                .AddHttpClientInstrumentation()
                // Npgsql's own ActivitySource covers EVERY database call — EF Core writes and the raw-ADO view
                // reads — with one span per command incl. the (parameterized) SQL text. The plan's additional
                // OpenTelemetry.Instrumentation.EntityFrameworkCore is deliberately NOT wired: EF commands
                // execute through Npgsql, so it would emit a second, redundant span per write while adding a
                // beta package that still covers less (no raw ADO).
                .AddNpgsql()
                .AddOtlpExporter(options =>
                {
                    if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                    {
                        options.Endpoint = new Uri(otlpEndpoint);
                    }
                }))
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter(ForumMetrics.MeterName);  // without this the domain Meter exists but exports nothing

                // Metric→trace exemplars (Phase 10c correlation): histogram samples recorded inside a
                // sampled Activity carry its trace id, so Grafana's latency panels link straight to the
                // Tempo trace behind a spike. NOT the SDK default — since exemplars went stable the
                // default filter is AlwaysOff, and without this line Prometheus stores zero exemplars
                // (found live; needs the Prometheus exporter ≥ 1.16.0-beta.1 too — see the CPM comment).
                metrics.SetExemplarFilter(ExemplarFilterType.TraceBased);

                metrics.AddPrometheusExporter();
            });

        return builder;
    }
}
