using System.Text.Json;

using Forum.Common.Telemetry;

namespace Forum.Api.Extensions;

/// <summary>
/// A-3: the RUM Web Vitals beacon (host wiring like the realtime hub, not a module). The SPA's web-vitals
/// reporter posts one JSON body per finalized vital via <c>navigator.sendBeacon</c>; the handler records it
/// into the <see cref="ForumMetrics"/> histograms that mirror B's <c>web_vitals_*</c> series.
///
/// Deliberately permissive, mirroring B's <c>VitalsHandler</c>: malformed bodies, unknown metric names and
/// non-finite values are dropped silently with 204 — a flaky client must never produce error noise — while
/// label cardinality is guarded by allow-listing the metric name here and the rating in ForumMetrics.
/// Anonymous by design (field data arrives before/without login); sendBeacon cannot set Content-Type to
/// application/json, so the body is read manually instead of via model binding. The global per-IP rate
/// limiter covers the route (B uses a dedicated 120/min/IP beacon budget — A's global window is the same
/// order of magnitude, and a dropped beacon only loses a data point).
/// </summary>
public static class TelemetryExtensions
{
    /// <summary>sendBeacon bodies are tiny; cap the read so a bogus client cannot stream.</summary>
    private const int MaxBodyBytes = 4096;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static WebApplication MapForumTelemetry(this WebApplication app)
    {
        app.MapPost("/api/telemetry/vitals", static async (HttpContext context, ForumMetrics metrics) =>
            {
                VitalReport? report = null;
                try
                {
                    using var buffer = new MemoryStream();
                    await CopyCappedAsync(context.Request.Body, buffer, context.RequestAborted);
                    report = JsonSerializer.Deserialize<VitalReport>(buffer.GetBuffer().AsSpan(0, (int)buffer.Length), Json);
                }
                catch (JsonException)
                {
                    // Malformed body → drop silently.
                }

                if (report is { Value: var value } && double.IsFinite(value) && value >= 0)
                {
                    switch (report.Name)
                    {
                        case "CLS":
                            metrics.WebVitalCls(report.Rating ?? string.Empty, value);
                            break;
                        case "LCP" or "FCP" or "INP" or "TTFB":
                            // web-vitals reports timings in milliseconds; the histogram is in seconds.
                            metrics.WebVitalDuration(report.Name, report.Rating ?? string.Empty, value / 1000.0);
                            break;
                        default:
                            break; // unknown metric name → drop (cardinality guard)
                    }
                }

                return Results.NoContent();
            })
            .AllowAnonymous()
            .WithName("ReportWebVitals")
            .WithTags("Telemetry")
            .WithSummary("Records one browser-reported Core Web Vital (RUM field data; permissive 204 always).");

        return app;
    }

    private static async Task CopyCappedAsync(Stream source, MemoryStream destination, CancellationToken cancellationToken)
    {
        var chunk = new byte[1024];
        while (destination.Length <= MaxBodyBytes)
        {
            var read = await source.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                return;
            }

            destination.Write(chunk, 0, read);
        }

        throw new JsonException("body exceeds the beacon cap"); // handled as a silent drop
    }

    private sealed record VitalReport(string? Name, double Value, string? Rating);
}
