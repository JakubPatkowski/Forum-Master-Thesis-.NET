using Forum.SharedKernel.Domain;

using Microsoft.Extensions.DependencyInjection;

namespace Forum.Infrastructure.Messaging;

/// <summary>Resolves and invokes the registered <see cref="IDomainEventHandler{TEvent}"/> for each event. In-process, within the request scope.</summary>
internal sealed class DomainEventDispatcher : IDomainEventDispatcher
{
    private readonly IServiceProvider _provider;

    public DomainEventDispatcher(IServiceProvider provider) => _provider = provider;

    public async Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in domainEvents)
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
            var handleMethod = handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.Handle))!;

            foreach (var handler in _provider.GetServices(handlerType))
            {
                if (handler is null)
                {
                    continue;
                }

                await ((Task)handleMethod.Invoke(handler, [domainEvent, cancellationToken])!).ConfigureAwait(false);
            }
        }
    }
}
