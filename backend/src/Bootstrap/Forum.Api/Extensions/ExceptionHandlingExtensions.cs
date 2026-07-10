using Forum.Api.Middleware;
using Forum.Common.Correlation;
using Forum.Common.Telemetry;

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Forum.Api.Extensions;

/// <summary>Turns unhandled exceptions into RFC 7807 ProblemDetails responses. Expected failures use the Result pattern, not exceptions.</summary>
public static class ExceptionHandlingExtensions
{
    public static IServiceCollection AddForumProblemDetails(this IServiceCollection services)
    {
        // ONE choke point for every ProblemDetails body the host writes: ApiResults' Result→HTTP rejections
        // (ProblemHttpResult routes through IProblemDetailsService since .NET 8) AND the exception handler below.
        services.AddProblemDetails(static options => options.CustomizeProblemDetails = static context =>
        {
            var provider = context.HttpContext.RequestServices;

            // Explicit, not ambient: on the exception path the Serilog scope pushed by CorrelationIdMiddleware
            // has already unwound, so the id comes from the request-scoped context. The default writer already
            // adds `traceId`; `correlationId` makes "paste the id from the response, find every log line" work.
            var correlationId = provider.GetService<ICorrelationContext>()?.CorrelationId;
            if (!string.IsNullOrEmpty(correlationId))
            {
                context.ProblemDetails.Extensions["correlationId"] = correlationId;
            }

            // Expected rejections (the 404→403→422 mapper stamps errorType). Deliberately Debug + a bounded
            // counter, not Error: a 401/403 spike is a security signal for dashboards, a 422 spike a frontend
            // bug — neither is an application fault worth flooding Loki with at 150 VU.
            if (context.ProblemDetails.Status is { } status and < StatusCodes.Status500InternalServerError
                && context.ProblemDetails.Extensions.TryGetValue("errorType", out var errorTypeValue)
                && errorTypeValue is string errorType)
            {
                provider.GetService<ForumMetrics>()?.ApiRejection(status, errorType);

                var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("Forum.Api.Rejections");
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    context.ProblemDetails.Extensions.TryGetValue("code", out var code);
                    logger.LogDebug(
                        "{Method} {Path} rejected: {StatusCode} {ErrorType} ({ErrorCode}).",
                        context.HttpContext.Request.Method, context.HttpContext.Request.Path,
                        status, errorType, code);
                }
            }
        });
        // Outside Development, minimal-API binding failures short-circuit to a bare, body-less 400 by default;
        // throwing routes them through the handler below instead, so a malformed JSON body gets the same
        // RFC 7807 envelope (with correlationId) in every environment.
        services.Configure<RouteHandlerOptions>(static options => options.ThrowOnBadRequest = true);

        services.AddExceptionHandler<GlobalExceptionHandler>();
        return services;
    }
}

internal sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ForumMetrics _metrics;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(
        IProblemDetailsService problemDetailsService, ForumMetrics metrics, ILogger<GlobalExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _metrics = metrics;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // UseExceptionHandler cleared the response — headers included — before re-executing, so the correlation
        // header CorrelationIdMiddleware set is gone; put it back so even a 500 is traceable end-to-end. Logged
        // explicitly too: the middleware's ambient logging scope was disposed while the exception unwound
        // through it, so relying on LogContext here would silently drop the property.
        var correlationId = httpContext.RequestServices.GetService<ICorrelationContext>()?.CorrelationId;
        if (!string.IsNullOrEmpty(correlationId))
        {
            httpContext.Response.Headers[CorrelationIdMiddleware.HeaderName] = correlationId;
        }

        // A client that disconnected mid-request is not a system fault: no Error log, no error metric, no body
        // (nobody is left to read it). 499 is nginx's "client closed request" convention. Matters under k6 load,
        // where aborted requests are routine and would otherwise drown the "recent errors" panel.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            httpContext.Response.StatusCode = 499;
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Request {Method} {Path} was cancelled by the client. CorrelationId: {CorrelationId}",
                    httpContext.Request.Method, httpContext.Request.Path, correlationId);
            }

            return true;
        }

        // An unparsable request (malformed JSON body, oversized headers…) surfaces as BadHttpRequestException —
        // a client error, not a fault: 4xx + rejection accounting via the errorType extension above, so garbage
        // input never pollutes the unhandled-error metric or dashboard.
        if (exception is BadHttpRequestException badRequest)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation(
                    "Request {Method} {Path} was malformed: {Reason}. CorrelationId: {CorrelationId}",
                    httpContext.Request.Method, httpContext.Request.Path, badRequest.Message, correlationId);
            }

            httpContext.Response.StatusCode = badRequest.StatusCode;
            return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails =
                {
                    Title = "The request was malformed.",
                    Status = badRequest.StatusCode,
                    Extensions = { ["code"] = "bad_request", ["errorType"] = "BadRequest" },
                },
            });
        }

        _metrics.UnhandledError(Categorize(exception));
        _logger.LogError(
            exception,
            "Unhandled exception while processing {Method} {Path}. CorrelationId: {CorrelationId}",
            httpContext.Request.Method, httpContext.Request.Path, correlationId);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Title = "An unexpected error occurred.",
                Status = StatusCodes.Status500InternalServerError,
            },
        });
    }

    /// <summary>Closed, bounded category set — Prometheus labels must never grow with exception type names.</summary>
    private static string Categorize(Exception exception) => exception switch
    {
        NpgsqlException or DbUpdateException => ForumMetrics.ErrorCategoryDatabase,
        TimeoutException => ForumMetrics.ErrorCategoryTimeout,
        _ => ForumMetrics.ErrorCategoryOther,
    };
}
