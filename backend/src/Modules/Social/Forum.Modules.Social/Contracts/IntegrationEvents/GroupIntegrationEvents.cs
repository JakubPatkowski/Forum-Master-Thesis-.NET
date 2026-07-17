using Forum.Common.Messaging;

namespace Forum.Modules.Social.Contracts.IntegrationEvents;

/// <summary>Published when a group is created (its chat conversation shares the group's id).</summary>
public sealed record GroupCreatedIntegrationEvent(
    Ulid EventId, Ulid GroupId, Ulid OwnerId, DateTimeOffset OccurredOnUtc) : IIntegrationEvent;

/// <summary>Published when a group is renamed/redescribed/re-visibilitied or ownership is transferred.</summary>
public sealed record GroupUpdatedIntegrationEvent(
    Ulid EventId, Ulid GroupId, DateTimeOffset OccurredOnUtc) : IIntegrationEvent;

/// <summary>Published when a group is soft-deleted. Files detaches the group's icon on this.</summary>
public sealed record GroupDeletedIntegrationEvent(
    Ulid EventId, Ulid GroupId, Ulid DeletedBy, DateTimeOffset OccurredOnUtc) : IIntegrationEvent;

/// <summary>Published when a group invite is created.</summary>
public sealed record GroupInviteSentIntegrationEvent(
    Ulid EventId, Ulid InviteId, Ulid GroupId, Ulid InvitedUserId, Ulid InvitedBy, DateTimeOffset OccurredOnUtc)
    : IIntegrationEvent;

/// <summary>Published when an invite stops being pending: accepted, declined or cancelled.</summary>
public sealed record GroupInviteRespondedIntegrationEvent(
    Ulid EventId, Ulid InviteId, Ulid GroupId, Ulid InvitedUserId, Ulid InvitedBy, bool Accepted,
    DateTimeOffset OccurredOnUtc) : IIntegrationEvent;

/// <summary>Published when a user enters a group (accepted invite or public join).</summary>
public sealed record GroupMemberJoinedIntegrationEvent(
    Ulid EventId, Ulid GroupId, Ulid UserId, DateTimeOffset OccurredOnUtc) : IIntegrationEvent;

/// <summary>Published when a user leaves or is removed from a group.</summary>
public sealed record GroupMemberLeftIntegrationEvent(
    Ulid EventId, Ulid GroupId, Ulid UserId, bool Removed, Ulid? RemovedBy, DateTimeOffset OccurredOnUtc)
    : IIntegrationEvent;
