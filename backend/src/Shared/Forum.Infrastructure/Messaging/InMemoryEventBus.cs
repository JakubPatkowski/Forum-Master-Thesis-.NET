using Forum.Common.Messaging;

using Microsoft.Extensions.DependencyInjection;

namespace Forum.Infrastructure.Messaging;

/// <summary>
/// The in-process dispatch stage of the messaging backbone: fans an integration event out to every registered
/// <see cref="IIntegrationEventHandler{TEvent}"/>. Fed by the per-module RabbitMQ consumer hosts, which
/// deserialize wire-delivered outbox messages and publish them here — handlers never see the broker.
/// Registered scoped so the handlers resolve from the active consumer scope.
/// </summary>
internal sealed class InMemoryEventBus : IEventBus
{
    private readonly IServiceProvider _provider;

    public InMemoryEventBus(IServiceProvider provider) => _provider = provider;

    public async Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
        where TEvent : class, IIntegrationEvent
    {
        foreach (var handler in _provider.GetServices<IIntegrationEventHandler<TEvent>>())
        {
            await handler.HandleAsync(integrationEvent, cancellationToken).ConfigureAwait(false);
        }
    }
}
