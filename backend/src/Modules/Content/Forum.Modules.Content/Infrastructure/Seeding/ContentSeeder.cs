using Forum.Infrastructure.Seeding;
using Forum.Modules.Content.Domain.Categories;
using Forum.Modules.Content.Domain.Comments;
using Forum.Modules.Content.Domain.Threads;
using Forum.Modules.Content.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Forum.Modules.Content.Infrastructure.Seeding;

/// <summary>
/// Seeds <c>forum_content</c>: categories, tags, threads (+ thread_tags) and a nested comment tree. Runs after
/// Identity so owner references resolve. Builds aggregates directly (deterministic ids + audit, no events), in
/// batches with the change tracker cleared each time. The <c>search_tsv</c> column is filled automatically by its
/// row trigger on every insert — the seeder never writes it.
/// </summary>
internal sealed class ContentSeeder : IModuleSeeder
{
    private const int BatchSize = 1000;

    // Independent, fixed RNG streams: each concern is deterministic on its own and edits to one do not shift others.
    private const int CategoryPickSeed = 20260707;
    private const int ThreadBodySeed = 20260808;
    private const int CommentTreeSeed = 20260909;
    private const int CommentAuthorSeed = 20261010;

    private readonly ContentDbContext _db;
    private readonly ILogger<ContentSeeder> _logger;

    public ContentSeeder(ContentDbContext db, ILogger<ContentSeeder> logger)
    {
        _db = db;
        _logger = logger;
    }

    public int Order => 2;

    public async Task SeedAsync(SeedConfig config, CancellationToken cancellationToken)
    {
        var plan = SeedPlan.For(config.Profile);

        if (config.AllowTruncate)
        {
            await TruncateAsync(cancellationToken);
        }

        await SeedCategoriesAsync(plan, cancellationToken);
        await SeedTagsAsync(plan, cancellationToken);
        var threads = await SeedThreadsAsync(plan, cancellationToken);
        await SeedCommentsAsync(plan, threads, cancellationToken);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Content seeded: {Categories} categories, {Tags} tags, {Threads} threads, {Comments} comments.",
                plan.CategoryCount, plan.TagCount, plan.ThreadCount, plan.CommentCount);
        }
    }

    private async Task SeedCategoriesAsync(SeedPlan plan, CancellationToken cancellationToken)
    {
        var buffer = new List<Category>(plan.CategoryCount);
        for (var index = 0; index < plan.CategoryCount; index++)
        {
            var (slug, name, description) = CategoryIdentity(plan, index);
            var visibility = plan.IsPrivateCategory(index) ? Visibility.Private : Visibility.Public;
            var ownerId = SeedUlids.Create(SeedStreams.User, plan.CategoryOwnerIndex(index));

            buffer.Add(Category.Seed(
                SeedUlids.Create(SeedStreams.Category, index), slug, name, description, visibility, ownerId,
                SeedTime.At(SeedStreams.Category, index)));
        }

        await FlushAsync(buffer, cancellationToken);
    }

    private async Task SeedTagsAsync(SeedPlan plan, CancellationToken cancellationToken)
    {
        var buffer = new List<Tag>(Math.Min(plan.TagCount, BatchSize));
        for (var index = 0; index < plan.TagCount; index++)
        {
            var slug = plan.Profile == SeedProfile.Development ? $"tag{index + 1}" : $"tag-{index + 1:D3}";
            buffer.Add(Tag.Seed(SeedUlids.Create(SeedStreams.Tag, index), slug, slug));
            if (buffer.Count >= BatchSize)
            {
                await FlushAsync(buffer, cancellationToken);
            }
        }

        await FlushAsync(buffer, cancellationToken);
    }

    private async Task<IReadOnlyList<SeededThread>> SeedThreadsAsync(SeedPlan plan, CancellationToken cancellationToken)
    {
        var pickRandom = new Random(CategoryPickSeed);
        var bodyRandom = new Random(ThreadBodySeed);

        var seeded = new List<SeededThread>(plan.ThreadCount);
        var threadBuffer = new List<Thread>(BatchSize);
        var tagPairs = new List<ThreadTag>(BatchSize);

        for (var index = 0; index < plan.ThreadCount; index++)
        {
            var id = SeedUlids.Create(SeedStreams.Thread, index);
            var categoryIndex = PickCategory(pickRandom, plan);
            var isPrivate = plan.IsPrivateCategory(categoryIndex);

            // Threads in a private category are authored by its owner (who always has access) — plausible data,
            // never a row the live app's private-category gate would have rejected.
            var ownerIndex = isPrivate ? plan.CategoryOwnerIndex(categoryIndex) : pickRandom.Next(plan.UserCount);
            var ownerId = SeedUlids.Create(SeedStreams.User, ownerIndex);

            var isBenchmark = plan.Profile == SeedProfile.Benchmark;
            var isPinned = isBenchmark && index % 100 == 0;             // ~1% pinned
            var isDeleted = isBenchmark && index % 97 == 13;            // ~1% soft-deleted
            var title = plan.Profile == SeedProfile.Development
                ? $"Dev thread {index + 1:D2}"
                : $"Benchmark thread {index + 1:D4}";
            var wordCount = plan.Profile == SeedProfile.Development ? 40 : pickRandom.Next(120, 320);

            threadBuffer.Add(Thread.Seed(
                id, SeedUlids.Create(SeedStreams.Category, categoryIndex), ownerId,
                title, SeedText.Body(bodyRandom, title, wordCount), isPinned,
                SeedTime.At(SeedStreams.Thread, index), isDeleted));
            seeded.Add(new SeededThread(index, isDeleted));

            foreach (var tagIndex in PickTags(pickRandom, plan))
            {
                tagPairs.Add(new ThreadTag(id, SeedUlids.Create(SeedStreams.Tag, tagIndex)));
            }

            if (threadBuffer.Count >= BatchSize)
            {
                await FlushAsync(threadBuffer, cancellationToken);
            }
        }

        await FlushAsync(threadBuffer, cancellationToken);

        // thread_tags reference now-committed threads/tags — insert them after, batched.
        for (var offset = 0; offset < tagPairs.Count; offset += BatchSize)
        {
            var batch = tagPairs.GetRange(offset, Math.Min(BatchSize, tagPairs.Count - offset));
            await FlushAsync(batch, cancellationToken);
        }

        return seeded;
    }

    private async Task SeedCommentsAsync(
        SeedPlan plan, IReadOnlyList<SeededThread> threads, CancellationToken cancellationToken)
    {
        var liveThreadIndices = threads.Where(static thread => !thread.IsDeleted).Select(static thread => thread.Index).ToArray();
        if (liveThreadIndices.Length == 0)
        {
            return;
        }

        var treeRandom = new Random(CommentTreeSeed);
        var authorRandom = new Random(CommentAuthorSeed);

        // Per-thread record of created comments so replies can extend a real parent's materialized path.
        var byThread = new Dictionary<int, List<CommentNode>>();
        var buffer = new List<Comment>(BatchSize);

        for (var index = 0; index < plan.CommentCount; index++)
        {
            var id = SeedUlids.Create(SeedStreams.Comment, index);
            var threadIndex = liveThreadIndices[SeedDistribution.ZipfIndex(treeRandom, liveThreadIndices.Length)];
            var threadId = SeedUlids.Create(SeedStreams.Thread, threadIndex);

            if (!byThread.TryGetValue(threadIndex, out var siblings))
            {
                siblings = [];
                byThread[threadIndex] = siblings;
            }

            var (parentId, path, depth) = PlaceComment(treeRandom, plan, siblings, id);
            var ownerId = SeedUlids.Create(SeedStreams.User, authorRandom.Next(plan.UserCount));
            var isDeleted = plan.Profile == SeedProfile.Benchmark && index % 89 == 7; // ~1% soft-deleted

            buffer.Add(Comment.Seed(
                id, threadId, parentId, ownerId, SeedText.Sentence(authorRandom, 12), path, depth,
                SeedTime.At(SeedStreams.Comment, index), isDeleted));
            siblings.Add(new CommentNode(id, path, depth));

            if (buffer.Count >= BatchSize)
            {
                await FlushAsync(buffer, cancellationToken);
            }
        }

        await FlushAsync(buffer, cancellationToken);
    }

    // Chooses this comment's place in its thread's tree, honouring the depth distribution and the depth cap, and
    // building the materialized path exactly like Comment.CreateReply (parent.Path + "." + id).
    private static (Ulid? ParentId, string Path, int Depth) PlaceComment(
        Random random, SeedPlan plan, List<CommentNode> siblings, Ulid id)
    {
        var desiredDepth = plan.Profile == SeedProfile.Development ? DevelopmentDepth(random) : BenchmarkDepth(random);
        if (desiredDepth > 0)
        {
            var candidates = siblings.Where(node => node.Depth == desiredDepth - 1).ToArray();
            if (candidates.Length > 0)
            {
                var parent = candidates[random.Next(candidates.Length)];
                return (parent.Id, $"{parent.Path}.{id}", parent.Depth + 1);
            }
        }

        return (null, id.ToString(), 0); // root (or fell back to root: no eligible parent yet)
    }

    // Distribution across depths 0..4 (root..fourth reply): 60/25/10/4/1%, capped below Comment.MaxDepth.
    private static int BenchmarkDepth(Random random)
    {
        var roll = random.Next(100);
        return roll < 60 ? 0 : roll < 85 ? 1 : roll < 95 ? 2 : roll < 99 ? 3 : 4;
    }

    // Lighter nesting for the tiny dev set, but still exercises replies (path materialization).
    private static int DevelopmentDepth(Random random) => random.Next(3) == 0 ? 1 : 0;

    private static int PickCategory(Random random, SeedPlan plan)
    {
        // Zipf-ish skew: with 4+ categories, half the threads land in the hot head (first three).
        if (plan.CategoryCount <= 3)
        {
            return random.Next(plan.CategoryCount);
        }

        return random.NextDouble() < 0.5 ? random.Next(3) : random.Next(plan.CategoryCount);
    }

    private static HashSet<int> PickTags(Random random, SeedPlan plan)
    {
        var count = Math.Min(random.Next(0, 6), plan.TagCount); // 0..5 tags, capped by the tag pool
        var chosen = new HashSet<int>();
        while (chosen.Count < count)
        {
            chosen.Add(random.Next(plan.TagCount));
        }

        return chosen;
    }

    private static (string Slug, string Name, string Description) CategoryIdentity(SeedPlan plan, int index)
    {
        if (plan.Profile == SeedProfile.Development)
        {
            return index == 0
                ? ("general", "General", "General discussion for the development seed.")
                : ("private-club", "Private Club", "A members-only category (private visibility).");
        }

        var ordinal = index + 1;
        var visibility = plan.IsPrivateCategory(index) ? "private" : "public";
        return ($"bench-cat-{ordinal:D2}", $"Benchmark Category {ordinal:D2}", $"Seeded {visibility} benchmark category {ordinal:D2}.");
    }

    private async Task FlushAsync<T>(List<T> buffer, CancellationToken cancellationToken)
        where T : class
    {
        if (buffer.Count == 0)
        {
            return;
        }

        await _db.Set<T>().AddRangeAsync(buffer, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        _db.ChangeTracker.Clear();
        buffer.Clear();
    }

    private Task<int> TruncateAsync(CancellationToken cancellationToken) =>
        _db.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE forum_content.thread_tags, forum_content.comments, forum_content.threads,
                     forum_content.tags, forum_content.categories,
                     forum_content.outbox_messages, forum_content.inbox_messages
            RESTART IDENTITY CASCADE
            """,
            cancellationToken);

    private sealed record SeededThread(int Index, bool IsDeleted);

    private sealed record CommentNode(Ulid Id, string Path, int Depth);
}
