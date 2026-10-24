using Forum.Infrastructure.Seeding;

using Shouldly;

using Xunit;

namespace Forum.Api.Tests.Unit;

/// <summary>
/// Pure-logic tests for the deterministic seed generators (Phase 9b): the id/timestamp functions are reproducible
/// and the profile plan's counts and role/category conventions are internally consistent; plus the Benchmark social
/// graph (<see cref="BenchSocialGraph"/>) reproduces Architecture B's ring rule exactly. No database required.
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

    // ---- Benchmark social graph = Architecture B's seedbench.go rule (A/B deviation D6) ------------------------

    [Fact]
    public void Benchmark_plan_social_counts_agree_with_the_ring_rule()
    {
        var plan = SeedPlan.For(SeedProfile.Benchmark);

        plan.FriendshipCount.ShouldBe(2400);
        plan.PendingFriendshipCount.ShouldBe(80);
        plan.GroupCount.ShouldBe(0);
        plan.GroupInviteCount.ShouldBe(0);
        plan.DirectConversationCount.ShouldBe(800); // derived: 2 000 messages over 800 ring pairs touch every pair
        plan.MessageCount.ShouldBe(2000);

        // The locked counts and the graph rule must agree — SocialSeeder refuses to run otherwise.
        BenchSocialGraph.AcceptedPairs(plan.UserCount).Count.ShouldBe(plan.FriendshipCount);
        BenchSocialGraph.PendingPairs(plan.UserCount, plan.PendingFriendshipCount).Count.ShouldBe(plan.PendingFriendshipCount);
        BenchSocialGraph.DistinctDirectPairCount(plan.UserCount, plan.MessageCount).ShouldBe(plan.DirectConversationCount);

        // Development is untouched by the rule.
        var development = SeedPlan.For(SeedProfile.Development);
        (development.FriendshipCount, development.PendingFriendshipCount, development.GroupCount,
            development.DirectConversationCount, development.MessageCount).ShouldBe((4, 1, 2, 1, 12));
    }

    [Fact]
    public void Benchmark_accepted_friendships_are_a_ring_with_two_chords_and_no_duplicate_pair()
    {
        var pairs = BenchSocialGraph.AcceptedPairs(800);

        pairs.Count.ShouldBe(2400);
        pairs.Select(BenchSocialGraph.Normalize).Distinct().Count().ShouldBe(2400);
        pairs.ShouldAllBe(p => p.Requester != p.Addressee);
        pairs.ShouldAllBe(p => p.Requester >= 0 && p.Requester < 800 && p.Addressee >= 0 && p.Addressee < 800);

        // B's loop order: chord-major (1, 7, 40), user-minor; requester = i, addressee = (i + offset) mod n.
        pairs[0].ShouldBe(new SeedFriendPair(0, 1));
        pairs[799].ShouldBe(new SeedFriendPair(799, 0));
        pairs[800].ShouldBe(new SeedFriendPair(0, 7));
        pairs[1600].ShouldBe(new SeedFriendPair(0, 40));
        pairs[2399].ShouldBe(new SeedFriendPair(799, 39));

        // Two edges per chord per user → everyone has exactly six friends.
        pairs.SelectMany(p => new[] { p.Requester, p.Addressee })
            .GroupBy(user => user)
            .ShouldAllBe(group => group.Count() == 6);
    }

    [Fact]
    public void Benchmark_pending_requests_use_stride_3_offset_13_and_never_collide_with_a_friendship()
    {
        var accepted = BenchSocialGraph.AcceptedPairs(800).Select(BenchSocialGraph.Normalize).ToHashSet();
        var pending = BenchSocialGraph.PendingPairs(800, 80);

        pending.Count.ShouldBe(80);
        pending.Select(BenchSocialGraph.Normalize).Distinct().Count().ShouldBe(80);
        pending.ShouldAllBe(p => !accepted.Contains(BenchSocialGraph.Normalize(p)));

        // B: from = (k * 3) % n, to = (from + 13) % n — requester is `from`, addressee is `to`.
        for (var k = 0; k < pending.Count; k++)
        {
            pending[k].ShouldBe(new SeedFriendPair((k * 3) % 800, (k * 3 + 13) % 800));
        }
    }

    [Fact]
    public void Benchmark_messages_walk_the_ring_pairs_with_alternating_senders_between_friends()
    {
        var accepted = BenchSocialGraph.AcceptedPairs(800).Select(BenchSocialGraph.Normalize).ToHashSet();
        var messages = Enumerable.Range(0, 2000).Select(i => BenchSocialGraph.MessageAt(i, 800)).ToList();

        for (var i = 0; i < messages.Count; i++)
        {
            var (pair, sender, recipient) = messages[i];
            pair.ShouldBe(i % 800);

            // users[p] sends on even i, users[(p+1) mod n] on odd i (B: `if i%2 == 1 { from, to = b, a }`).
            var (a, b) = (pair, (pair + 1) % 800);
            (sender, recipient).ShouldBe(i % 2 == 0 ? (a, b) : (b, a));

            // Always friends — B's store-level DM authorization premise holds on A too.
            accepted.ShouldContain(BenchSocialGraph.Normalize(new SeedFriendPair(sender, recipient)));
        }

        // 2 000 messages over 800 pairs: every pair is touched (→ 800 conversations), the first 400 three times.
        var perPair = messages.GroupBy(m => m.Pair).ToDictionary(g => g.Key, g => g.Count());
        perPair.Count.ShouldBe(800);
        perPair.Count(kv => kv.Value == 3).ShouldBe(400);
        perPair.Count(kv => kv.Value == 2).ShouldBe(400);
        BenchSocialGraph.DistinctDirectPairCount(800, 2000).ShouldBe(800);
        BenchSocialGraph.DistinctDirectPairCount(800, 500).ShouldBe(500);
    }

    [Fact]
    public void Benchmark_read_markers_cover_every_fourth_ring_pair_up_to_its_last_message()
    {
        var readers = BenchSocialGraph.ReadMarkerPairs(800);

        readers.Count.ShouldBe(200);
        readers.ShouldAllBe(pair => pair % 4 == 0);
        readers[0].ShouldBe(0);
        readers[^1].ShouldBe(796);

        BenchSocialGraph.LastMessageIndex(0, 800, 2000).ShouldBe(1600);
        BenchSocialGraph.LastMessageIndex(399, 800, 2000).ShouldBe(1999);
        BenchSocialGraph.LastMessageIndex(400, 800, 2000).ShouldBe(1200);
        BenchSocialGraph.LastMessageIndex(799, 800, 2000).ShouldBe(1599);
        BenchSocialGraph.LastMessageIndex(700, 800, 500).ShouldBeNull();
    }

    [Fact]
    public void Small_rings_skip_self_edges_and_wrapping_chords_like_B()
    {
        // n = 5: chord 40 ≡ 0 is skipped outright; chords 1 and 7 ≡ 2 together give the complete graph K5.
        BenchSocialGraph.AcceptedPairs(5).Count.ShouldBe(10);

        // n = 2: one possible pair — the reverse edge and both chords collapse onto it (B's duplicate-edge skip).
        BenchSocialGraph.AcceptedPairs(2).Count.ShouldBe(1);
        BenchSocialGraph.DistinctDirectPairCount(2, 10).ShouldBe(1);

        // n = 1: nobody to befriend or message.
        BenchSocialGraph.AcceptedPairs(1).ShouldBeEmpty();
        BenchSocialGraph.PendingPairs(1, 5).ShouldBeEmpty();
        BenchSocialGraph.DistinctDirectPairCount(1, 10).ShouldBe(0);
    }
}
