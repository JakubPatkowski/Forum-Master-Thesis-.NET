namespace Forum.Infrastructure.Seeding;

/// <summary>
/// Which named dataset the offline seeder produces. Both profiles share identical seeding <em>logic</em>
/// (deterministic ULIDs, fixed timestamp base, one Argon2id hash reused) — only the row <em>counts</em> differ.
/// </summary>
public enum SeedProfile
{
    /// <summary>Tiny, fast dataset for the day-to-day <c>make api</c> dev loop (a handful of rows, &lt; 5 s).</summary>
    Development,

    /// <summary>Larger, deterministic dataset sized for a fair A/B benchmark against Architecture B.</summary>
    Benchmark,
}
