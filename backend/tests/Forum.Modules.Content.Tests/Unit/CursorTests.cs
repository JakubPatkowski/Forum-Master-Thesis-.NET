using Forum.Modules.Content.Application.Paging;

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
        var cursor = new ThreadSearchCursor(0.60858315f, DateTimeOffset.UtcNow, Ulid.NewUlid());

        var decoded = ThreadSearchCursor.TryDecode(cursor.Encode());

        decoded.ShouldBe(cursor);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bm90fGN1cnNvcg")] // "not|cursor" — right delimiter, wrong shape
    [InlineData("%%%")]
    public void Search_cursor_rejects_malformed_values(string value)
    {
        ThreadSearchCursor.TryDecode(value).ShouldBeNull();
    }
}
