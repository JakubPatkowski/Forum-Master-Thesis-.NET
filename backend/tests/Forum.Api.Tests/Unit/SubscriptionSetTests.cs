using Forum.Api.Realtime;

using Shouldly;

using Xunit;

namespace Forum.Api.Tests.Unit;

/// <summary>Per-connection subscription semantics: parsing, matching by category/thread/user, and the cap.</summary>
public sealed class SubscriptionSetTests
{
    private readonly Ulid _categoryId = Ulid.NewUlid();
    private readonly Ulid _threadId = Ulid.NewUlid();
    private readonly Ulid _userId = Ulid.NewUlid();

    private RealtimeNotification Notification(Ulid? threadId = null, Ulid? actorUserId = null) => new(
        new ChangeNotification("created", "thread", Ulid.NewUlid().ToString(), null, _categoryId.ToString()),
        _categoryId, threadId, actorUserId);

    [Theory]
    [InlineData("category")]
    [InlineData("thread")]
    [InlineData("user")]
    public void Valid_views_parse(string view)
    {
        // ViewKind is internal, so the expectation is derived rather than passed via InlineData.
        var expected = view switch
        {
            "category" => ViewKind.Category,
            "thread" => ViewKind.Thread,
            _ => ViewKind.User,
        };

        SubscriptionView.TryParse(view, _categoryId.ToString(), out var parsed).ShouldBeTrue();
        parsed.ShouldBe(new SubscriptionView(expected, _categoryId));
    }

    [Theory]
    [InlineData("feed", "01ARZ3NDEKTSV4RRFFQ69G5FAV")]
    [InlineData("category", "not-a-ulid")]
    [InlineData(null, "01ARZ3NDEKTSV4RRFFQ69G5FAV")]
    [InlineData("category", null)]
    public void Unknown_views_and_bad_ids_do_not_parse(string? view, string? id) =>
        SubscriptionView.TryParse(view, id, out _).ShouldBeFalse();

    [Fact]
    public void A_category_subscription_matches_events_in_that_category_only()
    {
        var set = new SubscriptionSet();
        set.TryAdd(new SubscriptionView(ViewKind.Category, _categoryId), 8).ShouldBeTrue();

        set.Matches(Notification()).ShouldBeTrue();
        set.Matches(new RealtimeNotification(
            Notification().Payload, Ulid.NewUlid(), null, null)).ShouldBeFalse();
    }

    [Fact]
    public void Thread_and_user_subscriptions_match_their_routing_fields()
    {
        var set = new SubscriptionSet();
        set.TryAdd(new SubscriptionView(ViewKind.Thread, _threadId), 8).ShouldBeTrue();
        set.TryAdd(new SubscriptionView(ViewKind.User, _userId), 8).ShouldBeTrue();

        set.Matches(Notification(threadId: _threadId)).ShouldBeTrue();
        set.Matches(Notification(actorUserId: _userId)).ShouldBeTrue();
        set.Matches(Notification(threadId: Ulid.NewUlid(), actorUserId: Ulid.NewUlid())).ShouldBeFalse();
    }

    [Fact]
    public void Unsubscribing_stops_matching()
    {
        var set = new SubscriptionSet();
        var view = new SubscriptionView(ViewKind.Category, _categoryId);
        set.TryAdd(view, 8).ShouldBeTrue();
        set.Remove(view);

        set.Matches(Notification()).ShouldBeFalse();
    }

    [Fact]
    public void The_cap_rejects_new_views_but_resubscribing_an_existing_one_succeeds()
    {
        var set = new SubscriptionSet();
        var first = new SubscriptionView(ViewKind.Category, _categoryId);
        set.TryAdd(first, maxSubscriptions: 1).ShouldBeTrue();

        set.TryAdd(new SubscriptionView(ViewKind.Thread, _threadId), maxSubscriptions: 1).ShouldBeFalse();
        set.TryAdd(first, maxSubscriptions: 1).ShouldBeTrue();
    }
}
