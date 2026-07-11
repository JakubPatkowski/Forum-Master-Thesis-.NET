namespace Forum.Infrastructure.Seeding;

/// <summary>
/// The single source of truth for a profile's row counts and the deterministic role/category conventions every
/// module seeder shares. Counts are the only thing that varies between profiles; the seeding logic is identical.
/// </summary>
/// <remarks>
/// Benchmark numbers were validated empirically against the §1 resource contract (a Benchmark seed measured
/// ~30 MB on-disk, comfortably inside the 1 GiB Postgres container limit) — see the Phase 9b summary. Keyset
/// pagination, FTS and the counter trigger are correctness-tested in Phases 2–4 and are NOT a function of scale,
/// so this size was chosen for a fair, memory-safe A/B run rather than "production scale".
/// </remarks>
public sealed class SeedPlan
{
    private SeedPlan(
        SeedProfile profile,
        int userCount, int adminCount, int moderatorCount, int blockedCount,
        int categoryCount, int privateCategoryCount, int membersPerPrivateCategory,
        int tagCount, int threadCount, int commentCount, int reactionCount,
        string password)
    {
        Profile = profile;
        UserCount = userCount;
        AdminCount = adminCount;
        ModeratorCount = moderatorCount;
        BlockedCount = blockedCount;
        CategoryCount = categoryCount;
        PrivateCategoryCount = privateCategoryCount;
        MembersPerPrivateCategory = membersPerPrivateCategory;
        TagCount = tagCount;
        ThreadCount = threadCount;
        CommentCount = commentCount;
        ReactionCount = reactionCount;
        Password = password;
    }

    public SeedProfile Profile { get; }

    public int UserCount { get; }

    public int AdminCount { get; }

    public int ModeratorCount { get; }

    public int BlockedCount { get; }

    public int CategoryCount { get; }

    public int PrivateCategoryCount { get; }

    public int MembersPerPrivateCategory { get; }

    public int TagCount { get; }

    public int ThreadCount { get; }

    public int CommentCount { get; }

    public int ReactionCount { get; }

    /// <summary>The single plaintext password hashed once (Argon2id) and reused for every seeded account.</summary>
    public string Password { get; }

    /// <summary>Global staff (admins + moderators). Category ownership round-robins over exactly this pool.</summary>
    public int StaffCount => AdminCount + ModeratorCount;

    public static SeedPlan For(SeedProfile profile) => profile switch
    {
        // Tiny fixed cast: admin, mod, alice, bob, charlie; general (public) + private-club (private).
        SeedProfile.Development => new SeedPlan(
            profile,
            userCount: 5, adminCount: 1, moderatorCount: 1, blockedCount: 0,
            categoryCount: 2, privateCategoryCount: 1, membersPerPrivateCategory: 1,
            tagCount: 4, threadCount: 10, commentCount: 10, reactionCount: 5,
            password: "Dev#Password1"),

        // Sized for the WSL2 / minikube 10 GiB budget (§1): big enough for Zipf hot-thread traffic, FTS corpus
        // hits and counter-trigger churn under k6, small enough to stay ~30 MB on disk.
        SeedProfile.Benchmark => new SeedPlan(
            profile,
            userCount: 800, adminCount: 2, moderatorCount: 10, blockedCount: 20,
            categoryCount: 12, privateCategoryCount: 4, membersPerPrivateCategory: 25,
            tagCount: 60, threadCount: 1600, commentCount: 9000, reactionCount: 15000,
            password: "Bench#Password1"),

        _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, "Unknown seed profile."),
    };

    /// <summary>Admins are the first block of users; role bands never overlap.</summary>
    public bool IsAdmin(int userIndex) => userIndex < AdminCount;

    public bool IsModerator(int userIndex) => userIndex >= AdminCount && userIndex < StaffCount;

    /// <summary>Blocked accounts sit just after staff — a status band, not a role.</summary>
    public bool IsBlocked(int userIndex) => userIndex >= StaffCount && userIndex < StaffCount + BlockedCount;

    /// <summary>The private categories are the last <see cref="PrivateCategoryCount"/> indices.</summary>
    public bool IsPrivateCategory(int categoryIndex) => categoryIndex >= CategoryCount - PrivateCategoryCount;

    /// <summary>Category ownership round-robins over the staff pool (owner always holds access).</summary>
    public int CategoryOwnerIndex(int categoryIndex) => StaffCount == 0 ? 0 : categoryIndex % StaffCount;

    /// <summary>
    /// The distinct, deterministic user indices granted a <c>moderate</c> ACL on one private category (its
    /// non-owner "members"). Global moderators already reach every private category via their role; these give
    /// specific ordinary users access to specific categories, exercising the SQL ACL resolver.
    /// </summary>
    public IReadOnlyList<int> PrivateCategoryMemberIndices(int categoryIndex)
    {
        var owner = CategoryOwnerIndex(categoryIndex);
        var members = new List<int>(MembersPerPrivateCategory);
        for (var step = 0; members.Count < MembersPerPrivateCategory && step < UserCount; step++)
        {
            var candidate = (categoryIndex * 13 + step * 7 + 3) % UserCount;
            if (candidate != owner && !members.Contains(candidate))
            {
                members.Add(candidate);
            }
        }

        return members;
    }
}
