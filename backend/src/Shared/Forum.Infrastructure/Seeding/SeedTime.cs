namespace Forum.Infrastructure.Seeding;

/// <summary>
/// Deterministic, wall-clock-free timestamps for seeded rows. Every entity gets a timestamp that is a pure
/// function of its (stream, index): distinct and monotonically increasing within a stream, so the ULID that
/// embeds it (see <see cref="SeedUlids"/>) and the <c>created_on_utc</c> column agree, and keyset ordering is
/// reproducible across machines and runs. No <c>DateTime.UtcNow</c> ever touches seed data.
/// </summary>
public static class SeedTime
{
    /// <summary>The instant all seeded timelines are anchored to.</summary>
    public static readonly DateTimeOffset Base = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The deterministic creation instant for the row at <paramref name="index"/> within <paramref name="stream"/>.</summary>
    public static DateTimeOffset At(string stream, int index) =>
        Base + StreamOffset(stream) + TimeSpan.FromSeconds((long)index * StepSeconds(stream));

    // A small per-stream head start so different streams do not all begin at the exact same instant.
    private static TimeSpan StreamOffset(string stream) => stream switch
    {
        SeedStreams.User => TimeSpan.Zero,
        SeedStreams.Category => TimeSpan.FromHours(1),
        SeedStreams.Tag => TimeSpan.FromHours(2),
        SeedStreams.Thread => TimeSpan.FromDays(1),
        SeedStreams.Comment => TimeSpan.FromDays(2),
        SeedStreams.Reaction => TimeSpan.FromDays(3),
        _ => TimeSpan.Zero,
    };

    // Per-stream spacing chosen so the largest Benchmark stream still lands comfortably inside 2026.
    private static long StepSeconds(string stream) => stream switch
    {
        SeedStreams.User => 3600,       // 1 h  → 1000 users ≈ 42 days
        SeedStreams.Category => 86400,  // 1 day
        SeedStreams.Tag => 60,
        SeedStreams.Thread => 1800,     // 30 m → 2000 threads ≈ 42 days
        SeedStreams.Comment => 120,     // 2 m  → 12000 comments ≈ 17 days
        SeedStreams.Reaction => 30,
        _ => 300,
    };
}
