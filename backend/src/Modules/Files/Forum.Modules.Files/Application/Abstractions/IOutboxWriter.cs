using Forum.Common.Messaging;

namespace Forum.Modules.Files.Application.Abstractions;

/// <summary>
/// Enqueues an integration event as a transactional-outbox row in the module's <c>outbox_messages</c> table. The row
/// is written in the same unit of work as the state change; the relay (Phase 6) publishes it to RabbitMQ.
/// </summary>
internal interface IOutboxWriter
{
    void Enqueue<TEvent>(TEvent integrationEvent)
        where TEvent : class, IIntegrationEvent;
}
