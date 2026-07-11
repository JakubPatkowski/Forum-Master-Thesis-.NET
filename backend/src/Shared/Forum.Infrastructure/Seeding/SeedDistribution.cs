namespace Forum.Infrastructure.Seeding;

/// <summary>Deterministic skew helpers shared by the module seeders (hot-thread / hot-target selection).</summary>
public static class SeedDistribution
{
    /// <summary>
    /// A Zipf-ish index in <c>[0, count)</c> biased toward low indices, so a small "hot" head accumulates most of
    /// the traffic (comment recursion, reaction counter churn) — squaring a uniform sample gives the skew.
    /// </summary>
    public static int ZipfIndex(Random random, int count)
    {
        if (count <= 1)
        {
            return 0;
        }

        var sample = random.NextDouble();
        var index = (int)(count * sample * sample);
        return index >= count ? count - 1 : index;
    }
}
