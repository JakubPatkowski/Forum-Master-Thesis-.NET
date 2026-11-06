using Forum.Modules.Content.Application.Paging;
using Forum.Modules.Content.Application.Threads;

using Shouldly;

using Xunit;

namespace Forum.Modules.Content.Tests.Unit;

public sealed class CursorTests
{
    [Fact]
    public void Feed_cursor_round_trips_all_sort_key_components()
    {
        var cursor = new ThreadFeedCursor(IsPinned: true, DateTimeOffset.UtcNow, Ulid.NewUlid());

        var decoded = ThreadFeedCursor.TryDecode(cursor.Encode());

        decoded.ShouldBe(cursor);
    }

    [Fact]
    public void Feed_cursor_preserves_full_timestamp_precision()
    {
        // Postgres timestamps carry microseconds; the cursor must resume at the exact same instant.
        var createdOnUtc = new DateTimeOffset(2026, 7, 1, 12, 34, 56, TimeSpan.Zero).AddTicks(1234567);
        var cursor = new ThreadFeedCursor(IsPinned: false, createdOnUtc, Ulid.NewUlid());

        var decoded = ThreadFeedCursor.TryDecode(cursor.Encode());

        decoded!.CreatedOnUtc.ShouldBe(createdOnUtc);
        decoded.CreatedOnUtc.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-base64url!!")]
    [InlineData("aGVsbG8")] // "hello" — decodes but is not a cursor payload
    [InlineData("MnwxfDNvbGRwYXJ0cw")] // wrong shape
    public void Feed_cursor_rejects_malformed_values(string value)
    {
        ThreadFeedCursor.TryDecode(value).ShouldBeNull();
    }

    [Fact]
    public void Search_cursor_round_trips_the_rank_exactly()
    {
        var cursor = ThreadSearchCursor.ForRelevance(0.60858315f, DateTimeOffset.UtcNow, Ulid.NewUlid());

        var decoded = ThreadSearchCursor.TryDecode(cursor.Encode());

        decoded.ShouldBe(cursor);
        decoded!.Sort.ShouldBe(ThreadSearchSort.Relevance);
    }

    [Fact]
    public void Newest_search_cursor_round_trips_without_a_rank()
    {
        var cursor = ThreadSearchCursor.ForNewest(DateTimeOffset.UtcNow, Ulid.NewUlid());

        var decoded = ThreadSearchCursor.TryDecode(cursor.Encode());

        decoded.ShouldBe(cursor);
        decoded!.Sort.ShouldBe(ThreadSearchSort.Newest);
        decoded.Rank.ShouldBeNull();
    }

    [Fact]
    public void Search_cursors_of_the_two_sorts_never_decode_as_each_other()
    {
        var createdOnUtc = DateTimeOffset.UtcNow;
        var id = Ulid.NewUlid();

        // The sort tag is part of the payload: the same resume point encodes differently per order.
        var relevance = ThreadSearchCursor.ForRelevance(0.25f, createdOnUtc, id).Encode();
        var newest = ThreadSearchCursor.ForNewest(createdOnUtc, id).Encode();

        relevance.ShouldNotBe(newest);
        ThreadSearchCursor.TryDecode(relevance)!.Sort.ShouldBe(ThreadSearchSort.Relevance);
        ThreadSearchCursor.TryDecode(newest)!.Sort.ShouldBe(ThreadSearchSort.Newest);
    }

    [Theory]
    [InlineData("relevance", "relevance")]
    [InlineData("NEWEST", "newest")]
    [InlineData(null, "relevance")]
    [InlineData("", "relevance")]
    public void Search_sort_parses_known_values_and_defaults_to_relevance(string? value, string expected)
    {
        ThreadSearchSortParser.TryParse(value)!.Value.ToValue().ShouldBe(expected);
    }

    [Fact]
    public void Search_sort_rejects_unknown_values()
    {
        ThreadSearchSortParser.TryParse("oldest").ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("bm90fGN1cnNvcg")] // "not|cursor" — right delimiter, wrong shape
    [InlineData("%%%")]
    [InlineData("MC42fDEyM3wwMUhaWg")] // the pre-v5 three-part relevance payload (no sort tag) is not accepted
    public void Search_cursor_rejects_malformed_values(string value)
    {
        ThreadSearchCursor.TryDecode(value).ShouldBeNull();
    }
}
