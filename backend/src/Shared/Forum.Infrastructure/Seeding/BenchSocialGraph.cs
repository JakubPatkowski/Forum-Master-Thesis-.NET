namespace Forum.Infrastructure.Seeding;

/// <summary>A directed friend edge over user indices: <see cref="Requester"/> asked, <see cref="Addressee"/> answers.</summary>
public readonly record struct SeedFriendPair(int Requester, int Addressee);

/// <summary>
/// One Benchmark direct message over user indices. <see cref="Pair"/> is the ring position <c>p</c> of its
/// conversation — the pair <c>(p, (p + 1) mod n)</c> — and doubles as the conversation's seed-stream index.
/// </summary>
public readonly record struct SeedDirectMessage(int Pair, int Sender, int Recipient);

/// <summary>
/// The Benchmark profile's social graph — a mirror of Architecture B's seeder rule (gomx
/// <c>app/forum/shared/seedbench.go</c>: <c>benchFriendships</c> + <c>benchDirectMessages</c>), so the J9 journey
/// (direct message) and the <c>forum_social</c> share of the database are symmetric in the A/B comparison
/// (deviation D6). Everything is a pure function of user indices in seed order (<c>0 … n-1</c>); no RNG is drawn:
/// <list type="bullet">
/// <item><description><b>Accepted friendships</b> — a ring with two chords: for every chord offset in {1, 7, 40}
/// (B's loop order) and every <c>i</c>, <c>users[i]</c> requests <c>users[(i + offset) mod n]</c> and the request
/// is accepted. An edge that lands on an already befriended pair — or on itself, for tiny <c>n</c> — is skipped,
/// exactly like B's duplicate-edge skip. For n = 800 that is 2 400 pairs, six friends per user.</description></item>
/// <item><description><b>Pending requests</b> — request <c>k</c> goes from <c>users[(3k) mod n]</c> to
/// <c>users[(3k + 13) mod n]</c>; self and already-connected pairs are skipped (B ignores the store error).
/// Offset 13 never coincides with a chord, so all 80 Benchmark requests materialise.</description></item>
/// <item><description><b>Direct messages</b> — message <c>i</c> lives on ring pair <c>p = i mod n</c>, between
/// <c>users[p]</c> and <c>users[(p + 1) mod n]</c>; <c>users[p]</c> sends when <c>i</c> is even, the other side
/// when <c>i</c> is odd. 2 000 messages over 800 pairs touch every pair (800 conversations), the first 400 three
/// times, the rest twice.</description></item>
/// <item><description><b>Read markers</b> — every fourth ring pair (<c>p = 0, 4, 8, …</c>) is marked read by
/// <c>users[p]</c> up to the pair's latest message (B's <c>MarkDMConversationRead</c> cursor).</description></item>
/// </list>
/// Text bodies are deliberately NOT part of the contract — only the counts and the graph are.
/// </summary>
public static class BenchSocialGraph
{
    /// <summary>Request <c>k</c> of the pending band starts at <c>users[(k · PendingStride) mod n]</c> …</summary>
    public const int PendingStride = 3;

    /// <summary>… and targets the user this many ring positions ahead.</summary>
    public const int PendingOffset = 13;

    /// <summary>Every <c>ReadMarkerStride</c>-th ring pair has its conversation marked read by its first user.</summary>
    public const int ReadMarkerStride = 4;

    /// <summary>Chord offsets of the accepted-friendship ring, in B's loop order.</summary>
    public static IReadOnlyList<int> AcceptedOffsets { get; } = [1, 7, 40];

    /// <summary>
    /// The accepted friendships in B's creation order (chord-major, user-minor): the position in this list is the
    /// friendship's seed-stream index. Requester = <c>i</c>, addressee = <c>(i + offset) mod n</c>.
    /// </summary>
    public static IReadOnlyList<SeedFriendPair> AcceptedPairs(int userCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(userCount);

        var seen = new HashSet<(int Lo, int Hi)>();
        var pairs = new List<SeedFriendPair>();
        foreach (var offset in AcceptedOffsets)
        {
            if (offset % userCount == 0)
            {
                continue; // The chord would loop every user onto itself (B: `off % n == 0`).
            }

            for (var i = 0; i < userCount; i++)
            {
                TryAdd(pairs, seen, i, (i + offset) % userCount);
            }
        }

        return pairs;
    }

    /// <summary>
    /// The pending requests that survive B's rule for <paramref name="pendingCount"/> attempts: attempt <c>k</c>
    /// is <c>users[(3k) mod n] → users[(3k + 13) mod n]</c>, dropped when it is a self-edge or already a pair
    /// (accepted or an earlier pending request). Position = seed-stream index AFTER the accepted pairs.
    /// </summary>
    public static IReadOnlyList<SeedFriendPair> PendingPairs(int userCount, int pendingCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(userCount);
        ArgumentOutOfRangeException.ThrowIfNegative(pendingCount);

        var seen = AcceptedPairs(userCount).Select(Normalize).ToHashSet();
        var pairs = new List<SeedFriendPair>(pendingCount);
        for (var k = 0; k < pendingCount; k++)
        {
            var from = (k * PendingStride) % userCount;
            TryAdd(pairs, seen, from, (from + PendingOffset) % userCount);
        }

        return pairs;
    }

    /// <summary>Message <paramref name="messageIndex"/>: its ring pair and who sends to whom.</summary>
    public static SeedDirectMessage MessageAt(int messageIndex, int userCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(messageIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(userCount);

        var pair = messageIndex % userCount;
        var first = pair;
        var second = (pair + 1) % userCount;
        return messageIndex % 2 == 0
            ? new SeedDirectMessage(pair, first, second)
            : new SeedDirectMessage(pair, second, first);
    }

    /// <summary>
    /// How many distinct direct conversations the first <paramref name="messageCount"/> messages touch — the
    /// Benchmark plan's <c>DirectConversationCount</c>, computed from the rule rather than guessed.
    /// </summary>
    public static int DistinctDirectPairCount(int userCount, int messageCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(userCount);
        ArgumentOutOfRangeException.ThrowIfNegative(messageCount);

        // Beyond n messages the ring repeats, so only the first min(messages, n) positions can add a pair.
        var pairs = new HashSet<(int Lo, int Hi)>();
        for (var i = 0; i < Math.Min(messageCount, userCount); i++)
        {
            var message = MessageAt(i, userCount);
            if (message.Sender != message.Recipient)
            {
                pairs.Add(Normalize(message.Sender, message.Recipient));
            }
        }

        return pairs.Count;
    }

    /// <summary>Ring positions whose conversation <c>users[p]</c> marks read: <c>0, 4, 8, …</c> below <paramref name="userCount"/>.</summary>
    public static IReadOnlyList<int> ReadMarkerPairs(int userCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(userCount);

        var pairs = new List<int>((userCount + ReadMarkerStride - 1) / ReadMarkerStride);
        for (var pair = 0; pair < userCount; pair += ReadMarkerStride)
        {
            pairs.Add(pair);
        }

        return pairs;
    }

    /// <summary>
    /// The index of the last message on ring pair <paramref name="pair"/>, or null when no message reaches that
    /// pair (B's <c>MarkDMConversationRead</c> then writes no cursor at all).
    /// </summary>
    public static int? LastMessageIndex(int pair, int userCount, int messageCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(userCount);
        ArgumentOutOfRangeException.ThrowIfNegative(pair);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(pair, userCount);
        ArgumentOutOfRangeException.ThrowIfNegative(messageCount);

        if (pair >= messageCount)
        {
            return null;
        }

        return pair + userCount * ((messageCount - 1 - pair) / userCount);
    }

    /// <summary>The unordered form of a pair, for duplicate detection in either direction.</summary>
    public static (int Lo, int Hi) Normalize(SeedFriendPair pair) => Normalize(pair.Requester, pair.Addressee);

    private static (int Lo, int Hi) Normalize(int a, int b) => (Math.Min(a, b), Math.Max(a, b));

    private static void TryAdd(List<SeedFriendPair> pairs, HashSet<(int Lo, int Hi)> seen, int requester, int addressee)
    {
        if (requester == addressee || !seen.Add(Normalize(requester, addressee)))
        {
            return;
        }

        pairs.Add(new SeedFriendPair(requester, addressee));
    }
}
