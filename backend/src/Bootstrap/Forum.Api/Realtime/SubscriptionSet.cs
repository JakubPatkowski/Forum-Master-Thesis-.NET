using System.Globalization;

namespace Forum.Api.Realtime;

/// <summary>The view kinds a client can subscribe to — mirrors the plan's per-view scoping (category/thread/user).</summary>
internal enum ViewKind
{
    Category,
    Thread,
    User,
}

/// <summary>One subscribed view: a kind plus the id of the category/thread/user it watches.</summary>
internal readonly record struct SubscriptionView(ViewKind Kind, Ulid Id)
{
    public static bool TryParse(string? view, string? id, out SubscriptionView parsed)
    {
        parsed = default;
        ViewKind? kind = view switch
        {
            "category" => ViewKind.Category,
            "thread" => ViewKind.Thread,
            "user" => ViewKind.User,
            _ => null,
        };
        if (kind is null || !Ulid.TryParse(id, CultureInfo.InvariantCulture, out var ulid))
        {
            return false;
        }

        parsed = new SubscriptionView(kind.Value, ulid);
        return true;
    }
}

/// <summary>
/// One connection's subscriptions. Thread-safe: the socket's read loop mutates it while the hub's dispatch
/// matches against it. Matching is pure set membership — visibility is the dispatcher's job, on every push.
/// </summary>
internal sealed class SubscriptionSet
{
    private readonly HashSet<SubscriptionView> _views = [];
    private readonly Lock _lock = new();

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _views.Count;
            }
        }
    }

    /// <summary>False when the cap is hit (the view is not added); re-subscribing an existing view succeeds.</summary>
    public bool TryAdd(SubscriptionView view, int maxSubscriptions)
    {
        lock (_lock)
        {
            return _views.Contains(view) || (_views.Count < maxSubscriptions && _views.Add(view));
        }
    }

    public void Remove(SubscriptionView view)
    {
        lock (_lock)
        {
            _views.Remove(view);
        }
    }

    /// <summary>True when any subscribed view covers the notification's category, thread or acting user.</summary>
    public bool Matches(RealtimeNotification notification)
    {
        lock (_lock)
        {
            if (_views.Contains(new SubscriptionView(ViewKind.Category, notification.CategoryId)))
            {
                return true;
            }

            if (notification.ThreadId is { } threadId
                && _views.Contains(new SubscriptionView(ViewKind.Thread, threadId)))
            {
                return true;
            }

            return notification.ActorUserId is { } userId
                && _views.Contains(new SubscriptionView(ViewKind.User, userId));
        }
    }
}
