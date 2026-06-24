using Forum.Common.Messaging;

using Microsoft.Extensions.DependencyInjection;

namespace Forum.Infrastructure.Messaging;

/// <summary>
/// Phase-0 in-process integration bus: publishes to any in-process <see cref="IIntegrationEventHandler{TEvent}"/>.
/// Replaced by the transactional-outbox + RabbitMQ relay in Phase 6; modules keep using <see cref="IEventBus"/> unchanged.
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
