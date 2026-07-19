using Forum.Modules.Social.Domain;
using Forum.SharedKernel.Domain;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Domain.Friendships;

/// <summary>
/// A directed friend request that becomes a mutual friendship on accept. Decline, cancel and unfriend all DELETE
/// the row (no declined/removed tombstones — the requester may try again later; abuse is bounded by privacy
/// settings and <see cref="SocialBlock"/>). One row per pair in either direction, enforced by a raw-SQL
/// LEAST/GREATEST unique index (see the InitialSocial migration).
/// </summary>
internal sealed class Friendship : AggregateRoot<Ulid>
{
    // EF materialization.
    private Friendship()
    {
    }

    private Friendship(Ulid id, Ulid requesterId, Ulid addresseeId) : base(id)
    {
        RequesterId = requesterId;
        AddresseeId = addresseeId;
        Status = FriendshipStatus.Pending;
    }

    public Ulid RequesterId { get; private set; }

    public Ulid AddresseeId { get; private set; }

    public FriendshipStatus Status { get; private set; }

    public DateTimeOffset? AcceptedOnUtc { get; private set; }

    public static Result<Friendship> Create(Ulid requesterId, Ulid addresseeId) =>
        requesterId == addresseeId
            ? Result.Failure<Friendship>(FriendshipErrors.SelfRequest)
            : Result.Success(new Friendship(Ulid.NewUlid(), requesterId, addresseeId));

    /// <summary>Only the addressee accepts, and only while pending.</summary>
    public Result Accept(Ulid userId, DateTimeOffset onUtc)
    {
        if (userId != AddresseeId)
        {
            return Result.Failure(FriendshipErrors.NotAddressee);
        }

        if (Status != FriendshipStatus.Pending)
        {
            return Result.Failure(FriendshipErrors.NotPending);
        }

        Status = FriendshipStatus.Accepted;
        AcceptedOnUtc = onUtc;
        return Result.Success();
    }

    /// <summary>True when <paramref name="userId"/> is either side of this friendship.</summary>
    public bool Involves(Ulid userId) => RequesterId == userId || AddresseeId == userId;

    /// <summary>The other side of the pair, from <paramref name="userId"/>'s point of view.</summary>
    public Ulid OtherThan(Ulid userId) => RequesterId == userId ? AddresseeId : RequesterId;

    /// <summary>Offline-seeder constructor: deterministic id + audit, optionally pre-accepted, no events.</summary>
    internal static Friendship Seed(
        Ulid id, Ulid requesterId, Ulid addresseeId, DateTimeOffset createdOnUtc, bool accepted)
    {
        var friendship = new Friendship(id, requesterId, addresseeId);
        friendship.SetCreated(createdOnUtc, requesterId);
        if (accepted)
        {
            friendship.Status = FriendshipStatus.Accepted;
            friendship.AcceptedOnUtc = createdOnUtc;
        }

        return friendship;
    }
}

/// <summary>Stored as text (Content's Visibility precedent), never as a PG enum.</summary>
internal enum FriendshipStatus
{
    Pending,
    Accepted,
}
