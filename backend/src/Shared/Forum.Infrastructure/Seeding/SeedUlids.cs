using System.Security.Cryptography;
using System.Text;

namespace Forum.Infrastructure.Seeding;

/// <summary>
/// Deterministic ULID generation for seeded rows. The ULID's timestamp comes from <see cref="SeedTime"/> (so ids
/// sort by creation order) and its 80 random bits are the first 10 bytes of <c>SHA-256(seed:stream:index)</c> —
/// a pure function of (stream, index), so any module seeder can reconstruct any entity's id from its stream and
/// index alone, without shared mutable RNG state and without a cross-module reference.
/// </summary>
/// <remarks>
/// <see cref="Ulid.NewUlid(DateTimeOffset)"/> would draw its random bits from a cryptographic RNG and break
/// reproducibility; the <c>randomness</c> overload used here is what makes the dataset stable across runs.
/// </remarks>
public static class SeedUlids
{
    private const int RngSeed = 20260707;

    /// <summary>The deterministic ULID for the row at <paramref name="index"/> within <paramref name="stream"/>.</summary>
    public static Ulid Create(string stream, int index)
    {
        Span<byte> digest = stackalloc byte[32];
        var input = Encoding.UTF8.GetBytes($"{RngSeed}:{stream}:{index}");
        SHA256.HashData(input, digest);
        return Ulid.NewUlid(SeedTime.At(stream, index), digest[..10]);
    }
}
