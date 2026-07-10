using Forum.Api.Correlation;
using Forum.Api.Extensions;
using Forum.Common.Correlation;
using Forum.TestUtilities;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NSubstitute;

using Shouldly;

using Xunit;

namespace Forum.Api.Tests.Unit;

/// <summary>
/// The client-disconnect carve-out: an OperationCanceledException with an aborted request is routine under load
/// (k6 at 150 VU), not a system fault — it must not reach Error level or the unhandled-error counter, or it
/// would drown the exact "recent errors" panel Phase 10c builds.
/// </summary>
public sealed class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task A_client_disconnect_is_information_level_and_never_counted_as_an_error()
    {
        var (handler, logger, metrics, problemDetails) = CreateHandler();
        var context = CreateContext("corr-cancel-1");
        context.RequestAborted = new CancellationToken(canceled: true);

        var handled = await handler.TryHandleAsync(
            context, new OperationCanceledException(), CancellationToken.None);

        handled.ShouldBeTrue();
        context.Response.StatusCode.ShouldBe(499);
        logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Information);
        metrics.Total("forum.errors.unhandled").ShouldBe(0);
        await problemDetails.DidNotReceiveWithAnyArgs().TryWriteAsync(default!);
    }

    [Fact]
    public async Task A_cancellation_without_a_client_abort_is_still_an_unhandled_error()
    {
        var (handler, logger, metrics, _) = CreateHandler();
        var context = CreateContext("corr-cancel-2");

        await handler.TryHandleAsync(context, new OperationCanceledException(), CancellationToken.None);

        context.Response.StatusCode.ShouldBe(StatusCodes.Status500InternalServerError);
        logger.Entries.ShouldHaveSingleItem().Level.ShouldBe(LogLevel.Error);
        metrics.Total("forum.errors.unhandled").ShouldBe(1);
    }

    private static (GlobalExceptionHandler Handler, ListLogger Logger, MetricCapture Metrics, IProblemDetailsService ProblemDetails)
        CreateHandler()
    {
        var problemDetails = Substitute.For<IProblemDetailsService>();
        var logger = new ListLogger();
        var forumMetrics = TestMetrics.Create(out var meterScope);
        var capture = new MetricCapture(meterScope);
        return (new GlobalExceptionHandler(problemDetails, forumMetrics, logger), logger, capture, problemDetails);
    }

    private static DefaultHttpContext CreateContext(string correlationId)
    {
        var services = new ServiceCollection();
        services.AddScoped<ICorrelationContext, CorrelationContext>();
        var scope = services.BuildServiceProvider().CreateScope();
        scope.ServiceProvider.GetRequiredService<ICorrelationContext>().Set(correlationId);
        return new DefaultHttpContext { RequestServices = scope.ServiceProvider };
    }

    private sealed class ListLogger : ILogger<GlobalExceptionHandler>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
