using Forum.Api.Realtime;

using Shouldly;

using Xunit;

namespace Forum.Api.Tests.Unit;

/// <summary>Per-connection subscription semantics: parsing (incl. Social's group/conversation views), route
/// intersection matching, and the cap.</summary>
public sealed class SubscriptionSetTests
{
    private readonly Ulid _categoryId = Ulid.NewUlid();
    private readonly Ulid _threadId = Ulid.NewUlid();
    private readonly Ulid _userId = Ulid.NewUlid();

    private static RealtimeNotification Notification(params SubscriptionView[] routes) => new(
        new ChangeNotification("created", "thread", Ulid.NewUlid().ToString(), null, null),
        RealtimeVisibility.TargetUsers,
        routes);

    [Theory]
    [InlineData("category")]
    [InlineData("thread")]
    [InlineData("user")]
    [InlineData("group")]
    [InlineData("conversation")]
    public void Valid_views_parse(string view)
    {
        // ViewKind is internal, so the expectation is derived rather than passed via InlineData.
        var expected = view switch
        {
            "category" => ViewKind.Category,
            "thread" => ViewKind.Thread,
            "user" => ViewKind.User,
            "group" => ViewKind.Group,
            _ => ViewKind.Conversation,
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
    public void A_subscription_matches_only_notifications_routed_to_it()
    {
        var set = new SubscriptionSet();
        set.TryAdd(new SubscriptionView(ViewKind.Category, _categoryId), 8).ShouldBeTrue();

        set.MatchesAny(Notification(new SubscriptionView(ViewKind.Category, _categoryId)).Routes).ShouldBeTrue();
        set.MatchesAny(Notification(new SubscriptionView(ViewKind.Category, Ulid.NewUlid())).Routes).ShouldBeFalse();
        // Same id under a DIFFERENT view kind is a different subscription — no accidental cross-kind match.
        set.MatchesAny(Notification(new SubscriptionView(ViewKind.Group, _categoryId)).Routes).ShouldBeFalse();
    }

    [Fact]
    public void Any_route_hitting_any_subscribed_view_matches()
    {
        var set = new SubscriptionSet();
        set.TryAdd(new SubscriptionView(ViewKind.Thread, _threadId), 8).ShouldBeTrue();
        set.TryAdd(new SubscriptionView(ViewKind.User, _userId), 8).ShouldBeTrue();

        set.MatchesAny(Notification(
            new SubscriptionView(ViewKind.Category, _categoryId),
            new SubscriptionView(ViewKind.Thread, _threadId)).Routes).ShouldBeTrue();
        set.MatchesAny(Notification(new SubscriptionView(ViewKind.User, _userId)).Routes).ShouldBeTrue();
        set.MatchesAny(Notification(
            new SubscriptionView(ViewKind.Thread, Ulid.NewUlid()),
            new SubscriptionView(ViewKind.User, Ulid.NewUlid())).Routes).ShouldBeFalse();
    }

    [Fact]
    public void Unsubscribing_stops_matching()
    {
        var set = new SubscriptionSet();
        var view = new SubscriptionView(ViewKind.Category, _categoryId);
        set.TryAdd(view, 8).ShouldBeTrue();
        set.Remove(view);

        set.MatchesAny(Notification(view).Routes).ShouldBeFalse();
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
