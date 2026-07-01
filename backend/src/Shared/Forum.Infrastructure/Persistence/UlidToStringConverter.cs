using System.Globalization;

using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Forum.Infrastructure.Persistence;

/// <summary>
/// Maps the <see cref="Ulid"/> value type to its 26-char Crockford base32 text (ADR 0006). Apply via
/// <c>ConfigureConventions</c> so every id/reference column stores a sortable, log-readable ULID string.
/// </summary>
public sealed class UlidToStringConverter : ValueConverter<Ulid, string>
{
    public UlidToStringConverter()
        : base(value => value.ToString(), value => Ulid.Parse(value, CultureInfo.InvariantCulture))
    {
    }
}
