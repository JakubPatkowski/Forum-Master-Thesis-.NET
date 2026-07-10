using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text;
using System.Text.Json;

using Forum.Api.Correlation;
using Forum.Api.Extensions;
using Forum.Api.Middleware;
using Forum.Common.Correlation;
using Forum.Common.Http;
using Forum.Common.Telemetry;
using Forum.SharedKernel.Results;
using Forum.TestUtilities;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Core;
using Serilog.Events;

using Shouldly;

using Xunit;

namespace Forum.Api.Tests.Hosting;

/// <summary>
/// Regression tests for the Phase 9a error-handling fixes, against a minimal in-memory pipeline wired exactly
/// like Program.cs (UseExceptionHandler OUTSIDE CorrelationIdMiddleware). The correlation id must survive into
/// the unhandled-exception log line, the response header AND the ProblemDetails body — the middleware's ambient
/// Serilog scope pops while the exception unwinds, and UseExceptionHandler clears the buffered response headers,
/// so all three must be attached explicitly. Also proves that ApiResults' ProblemHttpResult flows through the
/// CustomizeProblemDetails hook (correlationId + rejection accounting) on this framework version.
/// </summary>
public sealed class ExceptionPipelineTests : IAsyncLifetime, IDisposable
{
    private readonly ConcurrentQueue<LogEvent> _logEvents = new();
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private MetricCapture _metrics = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        var serilog = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.Sink(new QueueSink(_logEvents))
            .CreateLogger();
        builder.Logging.ClearProviders();
        builder.Logging.AddSerilog(serilog, dispose: true);

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ForumMetrics>();
        builder.Services.AddScoped<ICorrelationContext, CorrelationContext>();
        builder.Services.AddForumProblemDetails();

        _app = builder.Build();
        _app.UseExceptionHandler();
        _app.UseMiddleware<CorrelationIdMiddleware>();
        _app.MapGet("/boom", static string () => throw new InvalidOperationException("kaboom"));
        _app.MapPost("/echo", static (EchoBody body) => Results.Ok(body));
        _app.MapGet("/missing", static () => ApiResults.Problem(Error.NotFound("test.not_found", "Nothing here.")));

        await _app.StartAsync();
        _metrics = new MetricCapture(_app.Services.GetRequiredService<IMeterFactory>());
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        Dispose();
        await _app.DisposeAsync();
    }

    public void Dispose()
    {
        _metrics.Dispose();
        _client.Dispose();
    }

    [Fact]
    public async Task An_unhandled_exception_keeps_the_correlation_id_in_log_header_and_body()
    {
        var response = await SendWithCorrelationAsync(HttpMethod.Get, "/boom", "corr-unhandled-1");

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);

        // UseExceptionHandler cleared the response before re-executing — the handler must restore the header.
        response.Headers.GetValues(CorrelationIdMiddleware.HeaderName).ShouldHaveSingleItem()
            .ShouldBe("corr-unhandled-1");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("status").GetInt32().ShouldBe(500);
        body.RootElement.GetProperty("correlationId").GetString().ShouldBe("corr-unhandled-1");

        // The middleware's LogContext scope disposed during unwind — only an explicit property survives here.
        var logged = _logEvents.Where(static e => e.Level == LogEventLevel.Error).ShouldHaveSingleItem();
        logged.Exception.ShouldBeOfType<InvalidOperationException>();
        logged.Properties["CorrelationId"].ShouldBeOfType<ScalarValue>().Value.ShouldBe("corr-unhandled-1");

        var measurement = _metrics.For("forum.errors.unhandled").ShouldHaveSingleItem();
        measurement.Value.ShouldBe(1);
        measurement.Tags["category"].ShouldBe(ForumMetrics.ErrorCategoryOther);
    }

    [Fact]
    public async Task A_result_rejection_carries_the_correlation_id_and_feeds_the_rejection_counter()
    {
        var response = await SendWithCorrelationAsync(HttpMethod.Get, "/missing", "corr-reject-1");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("title").GetString().ShouldBe("Nothing here.");
        body.RootElement.GetProperty("code").GetString().ShouldBe("test.not_found");
        body.RootElement.GetProperty("errorType").GetString().ShouldBe("NotFound");
        body.RootElement.GetProperty("correlationId").GetString().ShouldBe("corr-reject-1");

        var measurement = _metrics.For("forum.api.rejections").ShouldHaveSingleItem();
        measurement.Tags["status"].ShouldBe(404);
        measurement.Tags["errorType"].ShouldBe("NotFound");

        // Expected failures never reach Error level or the unhandled counter.
        _metrics.Total("forum.errors.unhandled").ShouldBe(0);
        _logEvents.ShouldNotContain(static e => e.Level >= LogEventLevel.Error);
    }

    [Fact]
    public async Task A_malformed_json_body_is_a_rejection_not_an_unhandled_error()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/echo")
        {
            Content = new StringContent("{ this is not json", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "corr-badjson-1");
        var response = await _client.SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("errorType").GetString().ShouldBe("BadRequest");
        body.RootElement.GetProperty("correlationId").GetString().ShouldBe("corr-badjson-1");

        _metrics.Total("forum.errors.unhandled").ShouldBe(0);
        _metrics.For("forum.api.rejections").ShouldHaveSingleItem().Tags["errorType"].ShouldBe("BadRequest");
        _logEvents.ShouldNotContain(static e => e.Level >= LogEventLevel.Error);
    }

    private async Task<HttpResponseMessage> SendWithCorrelationAsync(HttpMethod method, string url, string correlationId)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, correlationId);
        return await _client.SendAsync(request);
    }

    private sealed record EchoBody(string Name);

    private sealed class QueueSink : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _events;

        public QueueSink(ConcurrentQueue<LogEvent> events) => _events = events;

        public void Emit(LogEvent logEvent) => _events.Enqueue(logEvent);
    }
}
