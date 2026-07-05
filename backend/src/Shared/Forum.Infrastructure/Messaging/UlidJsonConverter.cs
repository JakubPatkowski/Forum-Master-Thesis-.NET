using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Forum.Infrastructure.Messaging;

/// <summary>Serializes <see cref="Ulid"/> as its 26-char text form in integration-event payloads.</summary>
public sealed class UlidJsonConverter : JsonConverter<Ulid>
{
    public override Ulid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Ulid.Parse(reader.GetString(), CultureInfo.InvariantCulture);

    public override void Write(Utf8JsonWriter writer, Ulid value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
