using Forum.Common.Messaging;

namespace Forum.Modules.Social.Application.Abstractions;

/// <summary>Queues an integration event in the module's outbox, committed atomically with the state change.</summary>
internal interface IOutboxWriter
{
    void Enqueue<TEvent>(TEvent integrationEvent)
        where TEvent : class, IIntegrationEvent;
}
