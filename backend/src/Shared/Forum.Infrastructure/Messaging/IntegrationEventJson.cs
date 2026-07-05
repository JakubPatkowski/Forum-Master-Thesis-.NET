using System.Text.Json;

namespace Forum.Infrastructure.Messaging;

/// <summary>
/// The single source of truth for integration-event JSON: every module's outbox writer serializes with these
/// options and the consumer host deserializes with them, so the wire format cannot drift between modules.
/// </summary>
public static class IntegrationEventJson
{
    public static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        Converters = { new UlidJsonConverter() },
    };
}
