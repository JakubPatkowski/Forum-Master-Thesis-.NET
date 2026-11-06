using Forum.Api.Extensions;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

using Shouldly;

using Xunit;

namespace Forum.Api.Tests.Hosting;

/// <summary>
/// Observability:TracingEnabled (A/B alignment A-07): the measured benchmark runs with tracing OFF on both
/// architectures. "Off" must mean no tracer provider at all — an empty Otlp:Endpoint only redirects the
/// exporter to the SDK default and keeps 100% sampling. Metrics must keep working either way.
/// </summary>
public sealed class ObservabilityTracingToggleTests
{
    [Fact]
    public void Tracing_is_enabled_by_default()
    {
        using var app = Build();

        app.Services.GetService<TracerProvider>().ShouldNotBeNull();
        app.Services.GetService<MeterProvider>().ShouldNotBeNull();
    }

    [Fact]
    public void Tracing_can_be_switched_off_without_touching_metrics()
    {
        using var app = Build(("Observability:TracingEnabled", "false"));

        app.Services.GetService<TracerProvider>().ShouldBeNull();
        app.Services.GetService<MeterProvider>().ShouldNotBeNull();
    }

    private static WebApplication Build(params (string Key, string Value)[] settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(settings
            .Select(static s => new KeyValuePair<string, string?>(s.Key, s.Value)));
        builder.AddForumObservability();
        return builder.Build();
    }
}
