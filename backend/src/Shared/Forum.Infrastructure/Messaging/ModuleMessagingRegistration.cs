using Forum.Common.Messaging;
using Forum.Infrastructure.Messaging.RabbitMq;
using Forum.Infrastructure.Persistence;

using Microsoft.Extensions.DependencyInjection;

namespace Forum.Infrastructure.Messaging;

/// <summary>One consumed binding: the source module's exchange and the event type providing the routing key.</summary>
public sealed record ConsumedEvent(string SourceExchange, Type EventType);

/// <summary>Per-module messaging profile the relay and consumer host are parameterized with.</summary>
public sealed class ModuleMessagingOptions<TContext>
    where TContext : ForumDbContext
{
    /// <summary>Lowercased module name; doubles as the module's topic-exchange name (ADR 0009).</summary>
    public required string ModuleName { get; init; }

    public required IReadOnlyList<ConsumedEvent> ConsumedEvents { get; init; }
}

/// <summary>Collects the integration events a module consumes; only listed events get queue bindings (no dead bindings).</summary>
public sealed class ModuleMessagingBuilder
{
    private readonly List<ConsumedEvent> _consumed = [];

    /// <summary>
    /// Binds the module's work queue to <typeparamref name="TEvent"/>'s source-module exchange. The module must
    /// also register an <see cref="IIntegrationEventHandler{TEvent}"/> for it — the consumer host dispatches the
    /// deserialized event to every registered handler via <see cref="IEventBus"/>.
    /// </summary>
    public ModuleMessagingBuilder Consume<TEvent>()
        where TEvent : class, IIntegrationEvent
    {
        _consumed.Add(new ConsumedEvent(MessagingTopology.SourceExchange(typeof(TEvent)), typeof(TEvent)));
        return this;
    }

    internal IReadOnlyList<ConsumedEvent> Build() => _consumed;
}

/// <summary>
/// Wires a module into the messaging backbone: an outbox relay that publishes the module's
/// <c>outbox_messages</c> rows to its topic exchange, and — when the module consumes anything — a consumer host
/// that feeds wire-delivered events into the module's registered handlers with inbox dedupe.
/// </summary>
public static class ModuleMessagingRegistration
{
    public static IServiceCollection AddModuleMessaging<TContext>(
        this IServiceCollection services, string moduleName, Action<ModuleMessagingBuilder>? consumes = null)
        where TContext : ForumDbContext
    {
        var builder = new ModuleMessagingBuilder();
        consumes?.Invoke(builder);
        var consumedEvents = builder.Build();

        services.AddSingleton(new ModuleMessagingOptions<TContext>
        {
            ModuleName = moduleName,
            ConsumedEvents = consumedEvents,
        });

        services.AddHostedService<OutboxRelayService<TContext>>();
        if (consumedEvents.Count > 0)
        {
            services.AddHostedService<IntegrationEventConsumerService<TContext>>();
        }

        return services;
    }
}
