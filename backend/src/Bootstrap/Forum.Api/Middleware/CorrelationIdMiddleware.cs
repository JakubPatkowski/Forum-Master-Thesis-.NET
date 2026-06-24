using Forum.Common.Correlation;

using Serilog.Context;

namespace Forum.Api.Middleware;

/// <summary>Assigns a correlation id per request (honouring an inbound header), echoes it back, and enriches the logs.</summary>
internal sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ICorrelationContext correlationContext)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var inbound)
                            && !string.IsNullOrWhiteSpace(inbound)
            ? inbound.ToString()
            : Guid.NewGuid().ToString("N");

        correlationContext.Set(correlationId);
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
