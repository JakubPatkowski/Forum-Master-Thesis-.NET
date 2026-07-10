using System.Diagnostics;
using System.Text.Json;

using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Compact;

using Shouldly;

using Xunit;

namespace Forum.Api.Tests.Hosting;

/// <summary>
/// Pins the EXACT JSON keys the Production console formatter emits (appsettings.Production.json uses
/// RenderedCompactJsonFormatter). Phase 10c's Loki pipeline greps these literally — the LogQL error query
/// filters on <c>@l</c>, the Grafana derived field extracts <c>@tr</c>, and correlation search uses
/// <c>CorrelationId</c> — so a formatter or key change must fail a test, not a dashboard.
/// </summary>
public sealed class SerilogJsonShapeTests
{
    [Fact]
    public void The_production_formatter_emits_the_keys_phase_10c_relies_on()
    {
        var output = new StringWriter();
        using var activity = new Activity("test-activity");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();

        using (var logger = new LoggerConfiguration()
                   .Enrich.FromLogContext()
                   .WriteTo.Sink(new FormattingSink(new RenderedCompactJsonFormatter(), output))
                   .CreateLogger())
        using (LogContext.PushProperty("CorrelationId", "corr-json-1"))
        {
            logger.Error(
                new InvalidOperationException("kaboom"),
                "Unhandled exception while processing {Method} {Path}. CorrelationId: {CorrelationId}",
                "GET", "/boom", "corr-json-1");
        }

        using var line = JsonDocument.Parse(output.ToString());
        var root = line.RootElement;

        root.GetProperty("@t").GetDateTimeOffset();                       // timestamp
        root.GetProperty("@l").GetString().ShouldBe("Error");             // level — ABSENT on Information lines
        root.GetProperty("@m").GetString()
            .ShouldBe("Unhandled exception while processing \"GET\" \"/boom\". CorrelationId: \"corr-json-1\"");
        root.GetProperty("@x").GetString()!.ShouldContain("kaboom");      // exception with stack trace
        root.GetProperty("@tr").GetString().ShouldBe(activity.TraceId.ToString());
        root.GetProperty("@sp").GetString().ShouldBe(activity.SpanId.ToString());
        root.GetProperty("CorrelationId").GetString().ShouldBe("corr-json-1");
    }

    private sealed class FormattingSink : ILogEventSink
    {
        private readonly ITextFormatter _formatter;
        private readonly TextWriter _output;

        public FormattingSink(ITextFormatter formatter, TextWriter output)
        {
            _formatter = formatter;
            _output = output;
        }

        public void Emit(LogEvent logEvent) => _formatter.Format(logEvent, _output);
    }
}
