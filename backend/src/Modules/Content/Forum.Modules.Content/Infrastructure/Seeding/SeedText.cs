using System.Text;

namespace Forum.Modules.Content.Infrastructure.Seeding;

/// <summary>
/// Deterministic markdown body generator for seeded threads/comments. Draws from a fixed word bank with a caller
/// supplied <see cref="Random"/>, so output is reproducible. Every body contains the token <c>seeded</c> so the
/// FTS consistency check has a guaranteed corpus hit.
/// </summary>
/// <remarks>
/// <para>
/// <b>Selective search bank (harness h4, 2026-10-18, deviation D4 closed).</b> The bank is 300 words drawn with a
/// Zipf(s = 1.2) rank distribution, so a one-word search spans the whole selectivity range instead of matching
/// nearly every thread: rank 1 appears in ~100 % of 120–320-word bodies, rank 50 in ~39 %, rank 100 in ~20 %,
/// rank 300 in ~6 %; a query drawn uniformly from the bank hits a median of ~13 % of the corpus (~200 of 1600
/// threads), 39 % of queries hit under 10 %, 12 % hit over half. Architecture B's bench seeder
/// (<c>gomx/app/forum/shared/seedbench.go</c>) uses the SAME bank and the SAME rank distribution, and the k6
/// harness (<c>load/k6/lib/assets.js</c>) searches and writes with the same list — the three copies MUST stay
/// byte-identical (A: tsvector token match; B: ILIKE substring match — no bank word is a substring of another
/// bank word or of the fixed seed text, so both match the same documents).
/// </para>
/// </remarks>
internal static class SeedText
{
    private const double ZipfExponent = 1.2;

    private static readonly string[] Words =
    [
        "abandon", "ability", "abstract", "accent", "accept", "acoustic", "acrobat", "adapt",
        "address", "adopt", "advice", "aerial", "agenda", "agree", "alarm", "album",
        "alley", "alpine", "amber", "amount", "ancient", "animal", "ankle", "answer",
        "anvil", "apple", "apron", "arctic", "argue", "armor", "artist", "asteroid",
        "atlas", "atom", "auction", "august", "author", "avenue", "award", "axis",
        "bacon", "bagel", "balcony", "ballad", "banana", "banjo", "barley", "barrel",
        "battery", "beacon", "bedrock", "beetle", "berry", "bingo", "biscuit", "bison",
        "blizzard", "boulder", "bouquet", "breeze", "bridge", "bronze", "budget", "bugle",
        "bundle", "bunker", "butler", "cabin", "cactus", "camera", "candle", "canvas",
        "canyon", "caramel", "carbon", "carpet", "carrot", "castle", "cedar", "celery",
        "cement", "cereal", "chapel", "charcoal", "chimney", "cider", "cinema", "citrus",
        "cliff", "clinic", "clover", "cocoa", "collar", "colony", "compass", "condor",
        "coral", "cornet", "cotton", "cousin", "cradle", "crater", "cricket", "cuckoo",
        "cupboard", "cushion", "dagger", "daisy", "dancer", "decade", "denim", "desert",
        "diamond", "dinner", "dolphin", "donkey", "drawer", "dungeon", "eagle", "echo",
        "edge", "elder", "elephant", "emerald", "engine", "ermine", "escort", "ethics",
        "exhibit", "exile", "falcon", "fantasy", "feather", "fennel", "fiddle", "fjord",
        "flagon", "flannel", "fleet", "flute", "fossil", "freckle", "fungus", "funnel",
        "galaxy", "gander", "garnet", "gazelle", "giant", "glacier", "goblet", "gondola",
        "gospel", "grapefruit", "gravel", "guitar", "hamlet", "harbor", "harvest", "helmet",
        "hickory", "hinge", "hollow", "horizon", "hostel", "hurdle", "iceberg", "impala",
        "incense", "insect", "island", "jacket", "jaguar", "jelly", "jigsaw", "juniper",
        "kayak", "kettle", "kitten", "koala", "lagoon", "lapel", "lasso", "laurel",
        "leopard", "lichen", "lilac", "llama", "locket", "lumber", "lynx", "magnolia",
        "mango", "mantis", "marble", "marmot", "meadow", "melon", "mercury", "midnight",
        "mirror", "mitten", "monsoon", "moss", "motel", "napkin", "needle", "nickel",
        "noodle", "oasis", "olive", "omelet", "orbit", "osprey", "oyster", "paddle",
        "palace", "pansy", "papaya", "parrot", "peacock", "pelican", "pepper", "pewter",
        "pigeon", "pirate", "pistachio", "plaza", "pocket", "polar", "pomelo", "porch",
        "prairie", "pretzel", "pumpkin", "quail", "quartz", "rabbit", "radar", "raffle",
        "raisin", "raven", "recipe", "reef", "rifle", "rocket", "rosemary", "rubble",
        "saffron", "sandal", "satchel", "scarf", "sequoia", "shamrock", "sherbet", "shutter",
        "skillet", "slipper", "smoke", "spinach", "spruce", "squirrel", "stadium", "starling",
        "stool", "stork", "summit", "swallow", "tadpole", "tapestry", "teapot", "thimble",
        "thistle", "ticket", "timber", "tomato", "topaz", "tortoise", "trombone", "trophy",
        "tulip", "tunnel", "turnip", "turtle", "umbrella", "urchin", "vanilla", "verandah",
        "viking", "vinegar", "violet", "waffle", "walrus", "wardrobe", "wasabi", "wicker",
        "window", "wizard", "yogurt", "zephyr",
    ];

    /// <summary>Cumulative Zipf(s) probabilities over the ranks 1..N (rank r has weight 1 / r^s).</summary>
    private static readonly double[] ZipfCdf = BuildZipfCdf(Words.Length, ZipfExponent);

    /// <summary>Builds a markdown body of roughly <paramref name="targetWords"/> words (a heading + paragraph).</summary>
    public static string Body(Random random, string title, int targetWords)
    {
        var builder = new StringBuilder(targetWords * 8);
        builder.Append("# ").Append(title).Append("\n\nseeded ");

        for (var i = 0; i < targetWords; i++)
        {
            builder.Append(ZipfWord(random));
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
            builder.Append(' ').Append(ZipfWord(random));
        }

        return builder.Append('.').ToString();
    }

    /// <summary>The bank, in rank order (rank 1 first) — exposed for the seed tests and the harness cross-check.</summary>
    public static IReadOnlyList<string> WordBank => Words;

    /// <summary>Rank (0-based) for a uniform sample: inverse CDF of the Zipf distribution (binary search).</summary>
    public static int ZipfRank(double uniform)
    {
        var index = Array.BinarySearch(ZipfCdf, uniform);
        if (index < 0)
        {
            index = ~index;
        }

        return index >= ZipfCdf.Length ? ZipfCdf.Length - 1 : index;
    }

    private static string ZipfWord(Random random) => Words[ZipfRank(random.NextDouble())];

    private static double[] BuildZipfCdf(int count, double exponent)
    {
        var cdf = new double[count];
        var total = 0.0;
        for (var rank = 1; rank <= count; rank++)
        {
            total += 1.0 / Math.Pow(rank, exponent);
        }

        var running = 0.0;
        for (var rank = 1; rank <= count; rank++)
        {
            running += 1.0 / Math.Pow(rank, exponent) / total;
            cdf[rank - 1] = running;
        }

        cdf[count - 1] = 1.0; // guard against floating-point drift at the top end
        return cdf;
    }
}
