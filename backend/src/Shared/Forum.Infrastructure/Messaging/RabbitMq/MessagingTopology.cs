namespace Forum.Infrastructure.Messaging.RabbitMq;

/// <summary>
/// The broker naming scheme (ADR 0009): one durable topic exchange per source module (its lowercased name,
/// e.g. <c>content</c>), routing key = the event's short CLR type name. Each consuming module owns one durable
/// work queue plus a TTL retry queue and a poison parking queue, chained through the default exchange:
/// a rejected delivery dead-letters into the retry queue, sits out the retry delay, and dead-letters back.
/// </summary>
public static class MessagingTopology
{
    public static string RoutingKey(Type eventType) => eventType.Name;

    /// <summary>
    /// Derives the source module's exchange from the event contract's namespace
    /// (<c>Forum.Modules.&lt;Module&gt;.Contracts…</c>), so a binding can never point at a typo'd exchange.
    /// </summary>
    public static string SourceExchange(Type eventType)
    {
        var parts = (eventType.Namespace ?? string.Empty).Split('.');
        if (parts is ["Forum", "Modules", var module, "Contracts", ..])
        {
            return module.ToLowerInvariant();
        }

        throw new InvalidOperationException(
            $"'{eventType.FullName}' is not a module Contracts type; integration events must live in Forum.Modules.<Module>.Contracts.");
    }

    public static string EventsQueue(string moduleName) => $"{moduleName}.events";

    public static string RetryQueue(string moduleName) => $"{moduleName}.events.retry";

    public static string PoisonQueue(string moduleName) => $"{moduleName}.events.poison";
}
