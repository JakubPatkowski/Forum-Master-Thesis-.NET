using Forum.Common.Correlation;

namespace Forum.Api.Correlation;

/// <summary>Request-scoped holder for the correlation id, populated by <c>CorrelationIdMiddleware</c>.</summary>
internal sealed class CorrelationContext : ICorrelationContext
{
    public string CorrelationId { get; private set; } = string.Empty;

    public void Set(string correlationId) => CorrelationId = correlationId;
}
