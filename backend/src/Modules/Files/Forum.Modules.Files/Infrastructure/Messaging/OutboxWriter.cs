using System.Text.Json;

using Forum.Common.Messaging;
using Forum.Infrastructure.Messaging.Outbox;
using Forum.Modules.Files.Application.Abstractions;
using Forum.Modules.Files.Infrastructure.Persistence;

namespace Forum.Modules.Files.Infrastructure.Messaging;

/// <summary>
/// Writes integration events as <c>outbox_messages</c> rows on the Files DbContext, so they commit in the same
/// transaction as the state change. The Phase 6 relay publishes and stamps them.
/// </summary>
internal sealed class OutboxWriter : IOutboxWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new UlidJsonConverter() },
    };

    private readonly FilesDbContext _db;

    public OutboxWriter(FilesDbContext db) => _db = db;

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
