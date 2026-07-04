using System.Text.Json;

using Forum.Common.Messaging;
using Forum.Infrastructure.Messaging.Outbox;
using Forum.Modules.Engagement.Application.Abstractions;
using Forum.Modules.Engagement.Infrastructure.Persistence;

namespace Forum.Modules.Engagement.Infrastructure.Messaging;

/// <summary>
/// Writes integration events as <c>outbox_messages</c> rows on the Engagement DbContext, so they commit in the
/// same transaction as the state change. The Phase 6 relay publishes and stamps them.
/// </summary>
internal sealed class OutboxWriter : IOutboxWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new UlidJsonConverter() },
    };

    private readonly EngagementDbContext _db;

    public OutboxWriter(EngagementDbContext db) => _db = db;

    public void Enqueue<TEvent>(TEvent integrationEvent)
        where TEvent : class, IIntegrationEvent
    {
        var eventType = integrationEvent.GetType();
        _db.OutboxMessages.Add(new OutboxMessage
        {
            Id = integrationEvent.EventId,
            Type = eventType.FullName ?? eventType.Name,
            Payload = JsonSerializer.Serialize(integrationEvent, eventType, SerializerOptions),
            OccurredOnUtc = integrationEvent.OccurredOnUtc,
        });
    }
}
