using Forum.Common.Messaging;

namespace Forum.Modules.Social.Contracts.IntegrationEvents;

/// <summary>Published when a friend request is created. Routed to both users' realtime views.</summary>
public sealed record FriendRequestSentIntegrationEvent(
    Ulid EventId, Ulid FriendshipId, Ulid RequesterId, Ulid AddresseeId, DateTimeOffset OccurredOnUtc)
    : IIntegrationEvent;

/// <summary>Published when the addressee accepts a friend request.</summary>
public sealed record FriendRequestAcceptedIntegrationEvent(
    Ulid EventId, Ulid FriendshipId, Ulid RequesterId, Ulid AddresseeId, DateTimeOffset OccurredOnUtc)
    : IIntegrationEvent;

/// <summary>Published when a pending request is declined by the addressee or cancelled by the requester.</summary>
public sealed record FriendRequestDeclinedIntegrationEvent(
    Ulid EventId, Ulid FriendshipId, Ulid RequesterId, Ulid AddresseeId, DateTimeOffset OccurredOnUtc)
    : IIntegrationEvent;

/// <summary>Published when an accepted friendship is removed by either side.</summary>
public sealed record FriendRemovedIntegrationEvent(
    Ulid EventId, Ulid FriendshipId, Ulid RequesterId, Ulid AddresseeId, DateTimeOffset OccurredOnUtc)
    : IIntegrationEvent;
