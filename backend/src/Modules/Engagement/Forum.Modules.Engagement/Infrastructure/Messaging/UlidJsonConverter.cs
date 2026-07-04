using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Forum.Modules.Engagement.Infrastructure.Messaging;

/// <summary>Serializes <see cref="Ulid"/> as its 26-char text form in outbox payloads.</summary>
internal sealed class UlidJsonConverter : JsonConverter<Ulid>
{
    public override Ulid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Ulid.Parse(reader.GetString(), CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, Ulid value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
