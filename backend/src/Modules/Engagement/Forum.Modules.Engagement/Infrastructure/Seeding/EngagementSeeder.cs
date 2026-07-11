using Forum.Infrastructure.Seeding;
using Forum.Modules.Engagement.Domain.Reactions;
using Forum.Modules.Engagement.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Forum.Modules.Engagement.Infrastructure.Seeding;

/// <summary>
/// Seeds <c>forum_engagement.reactions</c> (deterministic 'like's, Zipf-skewed toward hot threads with a comment
/// share). Runs last, so thread/comment targets already exist. The <c>reaction_counts</c> tallies are maintained
/// automatically by the row trigger on each insert — the seeder never writes them. Reaction identity is the
/// composite (user, target, kind) key, so a HashSet guards against duplicate likes.
/// </summary>
internal sealed class EngagementSeeder : IModuleSeeder
{
    private const int BatchSize = 1000;
    private const int ReactionSeed = 20261111;
    private const double ThreadShare = 0.75; // 75% of reactions land on threads, the rest on comments

    private readonly EngagementDbContext _db;
    private readonly ILogger<EngagementSeeder> _logger;

    public EngagementSeeder(EngagementDbContext db, ILogger<EngagementSeeder> logger)
    {
        _db = db;
        _logger = logger;
    }

    public int Order => 3;

    public async Task SeedAsync(SeedConfig config, CancellationToken cancellationToken)
    {
        var plan = SeedPlan.For(config.Profile);

        if (config.AllowTruncate)
        {
            await TruncateAsync(cancellationToken);
        }

        if (plan.ReactionCount == 0 || plan.ThreadCount == 0)
        {
            return;
        }

        var random = new Random(ReactionSeed);
        var seen = new HashSet<(int User, bool OnThread, int Target)>();
        var buffer = new List<Reaction>(BatchSize);

        var written = 0;
        var maxAttempts = (plan.ReactionCount * 20) + 1000; // give the de-dupe room without ever looping forever
        for (var attempt = 0; written < plan.ReactionCount && attempt < maxAttempts; attempt++)
        {
            var userIndex = random.Next(plan.UserCount);
            var onThread = plan.CommentCount == 0 || random.NextDouble() < ThreadShare;
            var targetIndex = onThread
                ? SeedDistribution.ZipfIndex(random, plan.ThreadCount)
                : SeedDistribution.ZipfIndex(random, plan.CommentCount);

            if (!seen.Add((userIndex, onThread, targetIndex)))
            {
                continue;
            }

            var targetType = onThread ? ReactionTargetType.Thread : ReactionTargetType.Comment;
            var targetId = SeedUlids.Create(onThread ? SeedStreams.Thread : SeedStreams.Comment, targetIndex);

            buffer.Add(new Reaction(
                SeedUlids.Create(SeedStreams.User, userIndex), targetType, targetId, ReactionTypes.Like,
                SeedTime.At(SeedStreams.Reaction, written)));
            written++;

            if (buffer.Count >= BatchSize)
            {
                await FlushAsync(buffer, cancellationToken);
            }
        }

        await FlushAsync(buffer, cancellationToken);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Engagement seeded: {Reactions} reactions (target {Target}).", written, plan.ReactionCount);
        }
    }

    private async Task FlushAsync(List<Reaction> buffer, CancellationToken cancellationToken)
    {
        if (buffer.Count == 0)
        {
            return;
        }

        await _db.Reactions.AddRangeAsync(buffer, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        _db.ChangeTracker.Clear();
        buffer.Clear();
    }

    private Task<int> TruncateAsync(CancellationToken cancellationToken) =>
        // reaction_counts is truncated explicitly: TRUNCATE does not fire the per-row trigger that maintains it.
        _db.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE forum_engagement.reactions, forum_engagement.reaction_counts,
                     forum_engagement.outbox_messages, forum_engagement.inbox_messages
            RESTART IDENTITY CASCADE
            """,
            cancellationToken);
}
