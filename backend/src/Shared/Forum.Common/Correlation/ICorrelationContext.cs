namespace Forum.Common.Correlation;

/// <summary>Per-request correlation id, propagated to logs, traces and outgoing calls.</summary>
public interface ICorrelationContext
{
    string CorrelationId { get; }

    void Set(string correlationId);
}
