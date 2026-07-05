using System.Text.Json;

using Forum.Common.Correlation;
using Forum.Common.Messaging;
using Forum.Infrastructure.Messaging;
using Forum.Infrastructure.Messaging.Outbox;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Infrastructure.Persistence;

namespace Forum.Modules.Content.Infrastructure.Messaging;

/// <summary>
/// Writes integration events as <c>outbox_messages</c> rows on the Content DbContext, so they commit in the same
/// transaction as the state change. The relay publishes and stamps them; the correlation id rides along so the
/// consumer side logs under the originating request's id.
/// </summary>
internal sealed class OutboxWriter : IOutboxWriter
{
    private readonly ContentDbContext _db;
    private readonly ICorrelationContext _correlation;

    public OutboxWriter(ContentDbContext db, ICorrelationContext correlation)
    {
        _db = db;
        _correlation = correlation;
    }

    public void Enqueue<TEvent>(TEvent integrationEvent)
        where TEvent : class, IIntegrationEvent
    {
        var eventType = integrationEvent.GetType();
        _db.OutboxMessages.Add(new OutboxMessage
        {
            Id = integrationEvent.EventId,
            Type = eventType.FullName ?? eventType.Name,
            Payload = JsonSerializer.Serialize(integrationEvent, eventType, IntegrationEventJson.SerializerOptions),
            OccurredOnUtc = integrationEvent.OccurredOnUtc,
            CorrelationId = string.IsNullOrEmpty(_correlation.CorrelationId) ? null : _correlation.CorrelationId,
        });
    }
}
