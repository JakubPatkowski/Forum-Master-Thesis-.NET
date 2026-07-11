using System.Text;

namespace Forum.Modules.Content.Infrastructure.Seeding;

/// <summary>
/// Deterministic markdown body generator for seeded threads/comments. Draws from a fixed word bank with a caller
/// supplied <see cref="Random"/>, so output is reproducible. Every body contains the token <c>seeded</c> so the
/// FTS consistency check has a guaranteed corpus hit.
/// </summary>
internal static class SeedText
{
    private static readonly string[] Words =
    [
        "forum", "thread", "comment", "keyset", "pagination", "benchmark", "architecture", "module",
        "postgres", "rabbitmq", "outbox", "reaction", "moderator", "category", "markdown", "latency",
        "throughput", "cluster", "kubernetes", "observability", "trace", "metric", "cache", "index",
        "aggregate", "domain", "event", "handler", "query", "keyset", "cursor", "tsvector",
    ];

    /// <summary>Builds a markdown body of roughly <paramref name="targetWords"/> words (a heading + paragraph).</summary>
    public static string Body(Random random, string title, int targetWords)
    {
        var builder = new StringBuilder(targetWords * 8);
        builder.Append("# ").Append(title).Append("\n\nseeded ");

        for (var i = 0; i < targetWords; i++)
        {
            builder.Append(Words[random.Next(Words.Length)]);
            builder.Append(i % 18 == 17 ? ".\n\n" : ' ');
        }

        return builder.Append('.').ToString();
    }

    /// <summary>Builds a short single-sentence comment body of roughly <paramref name="targetWords"/> words.</summary>
    public static string Sentence(Random random, int targetWords)
    {
        var builder = new StringBuilder(targetWords * 8).Append("seeded");
        for (var i = 0; i < targetWords; i++)
        {
            builder.Append(' ').Append(Words[random.Next(Words.Length)]);
        }

        return builder.Append('.').ToString();
    }
}
