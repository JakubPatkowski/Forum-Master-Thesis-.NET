namespace Forum.Api.Realtime;

/// <summary>Tuning for the WebSocket hub, bound from the "Realtime" configuration section.</summary>
public sealed class RealtimeOptions
{
    public const string SectionName = "Realtime";

    /// <summary>
    /// Lifetime of a connect ticket. Deliberately a handful of seconds: the client mints it immediately before
    /// opening the socket, so a leaked ticket (access log, browser history) dies almost instantly.
    /// </summary>
    public int TicketTtlSeconds { get; init; } = 30;

    /// <summary>Upper bound on views one connection may subscribe to (memory guard; the SPA needs a handful).</summary>
    public int MaxSubscriptionsPerConnection { get; init; } = 64;
}
