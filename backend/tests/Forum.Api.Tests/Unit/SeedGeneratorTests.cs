using Forum.Infrastructure.Seeding;

using Shouldly;

using Xunit;

namespace Forum.Api.Tests.Unit;

/// <summary>
/// Pure-logic tests for the deterministic seed generators (Phase 9b): the id/timestamp functions are reproducible
/// and the profile plan's counts and role/category conventions are internally consistent. No database required.
/// </summary>
public sealed class SeedGeneratorTests
{
    [Fact]
    public void SeedUlids_are_reproducible_for_the_same_stream_and_index()
    {
        // The whole determinism guarantee rests on this: (stream, index) → a stable ULID across calls/runs.
        SeedUlids.Create(SeedStreams.User, 0).ShouldBe(SeedUlids.Create(SeedStreams.User, 0));
        SeedUlids.Create(SeedStreams.Thread, 1499).ShouldBe(SeedUlids.Create(SeedStreams.Thread, 1499));
    }

    [Fact]
    public void SeedUlids_differ_by_index_and_by_stream()
    {
        SeedUlids.Create(SeedStreams.User, 0).ShouldNotBe(SeedUlids.Create(SeedStreams.User, 1));
        SeedUlids.Create(SeedStreams.User, 0).ShouldNotBe(SeedUlids.Create(SeedStreams.Thread, 0));
    }

    [Fact]
    public void SeedUlids_embed_the_matching_deterministic_timestamp_and_sort_by_index()
    {
        // The ULID timestamp equals SeedTime.At — so created_on_utc and the id agree, and ids sort by creation.
        var first = SeedUlids.Create(SeedStreams.Thread, 0);
        var later = SeedUlids.Create(SeedStreams.Thread, 1);

        first.Time.ShouldBe(SeedTime.At(SeedStreams.Thread, 0));
        later.Time.ShouldBe(SeedTime.At(SeedStreams.Thread, 1));
        string.CompareOrdinal(first.ToString(), later.ToString()).ShouldBeLessThan(0);
    }

    [Fact]
    public void A_thousand_ids_in_a_stream_are_distinct()
    {
        var ids = Enumerable.Range(0, 1000).Select(i => SeedUlids.Create(SeedStreams.Comment, i)).ToHashSet();
        ids.Count.ShouldBe(1000);
    }

    [Theory]
    [InlineData(SeedProfile.Development, 5, 2, 4, 10, 10)]
    [InlineData(SeedProfile.Benchmark, 800, 12, 60, 1600, 9000)]
    public void Profile_plan_exposes_the_locked_counts(
        SeedProfile profile, int users, int categories, int tags, int threads, int comments)
    {
        var plan = SeedPlan.For(profile);

        plan.UserCount.ShouldBe(users);
        plan.CategoryCount.ShouldBe(categories);
        plan.TagCount.ShouldBe(tags);
        plan.ThreadCount.ShouldBe(threads);
        plan.CommentCount.ShouldBe(comments);
    }

    [Theory]
    [InlineData(SeedProfile.Development)]
    [InlineData(SeedProfile.Benchmark)]
    public void Role_bands_never_overlap_and_cover_only_their_users(SeedProfile profile)
    {
        var plan = SeedPlan.For(profile);

        for (var i = 0; i < plan.UserCount; i++)
        {
            // Admin, moderator and blocked bands are mutually exclusive for every user index.
            var bands = new[] { plan.IsAdmin(i), plan.IsModerator(i), plan.IsBlocked(i) }.Count(flag => flag);
            bands.ShouldBeLessThanOrEqualTo(1);
        }

        Enumerable.Range(0, plan.UserCount).Count(plan.IsAdmin).ShouldBe(plan.AdminCount);
        Enumerable.Range(0, plan.UserCount).Count(plan.IsModerator).ShouldBe(plan.ModeratorCount);
        Enumerable.Range(0, plan.UserCount).Count(plan.IsBlocked).ShouldBe(plan.BlockedCount);
    }

    [Theory]
    [InlineData(SeedProfile.Development)]
    [InlineData(SeedProfile.Benchmark)]
    public void Private_categories_are_the_tail_and_their_members_are_distinct_non_owners(SeedProfile profile)
    {
        var plan = SeedPlan.For(profile);

        Enumerable.Range(0, plan.CategoryCount).Count(plan.IsPrivateCategory).ShouldBe(plan.PrivateCategoryCount);

        for (var categoryIndex = 0; categoryIndex < plan.CategoryCount; categoryIndex++)
        {
            if (!plan.IsPrivateCategory(categoryIndex))
            {
                continue;
            }

            var members = plan.PrivateCategoryMemberIndices(categoryIndex);
            members.Count.ShouldBe(plan.MembersPerPrivateCategory);
            members.Distinct().Count().ShouldBe(members.Count);
            members.ShouldNotContain(plan.CategoryOwnerIndex(categoryIndex));
            members.ShouldAllBe(index => index >= 0 && index < plan.UserCount);
        }
    }

    [Fact]
    public void Category_ownership_stays_within_the_staff_pool()
    {
        var plan = SeedPlan.For(SeedProfile.Benchmark);
        for (var categoryIndex = 0; categoryIndex < plan.CategoryCount; categoryIndex++)
        {
            plan.CategoryOwnerIndex(categoryIndex).ShouldBeLessThan(plan.StaffCount);
        }
    }
}
