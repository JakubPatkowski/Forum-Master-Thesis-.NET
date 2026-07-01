using Forum.SharedKernel.Domain;

namespace Forum.Modules.Identity.Domain.Users.Events;

/// <summary>Raised when an account is blocked. Translated to the <c>UserBlocked</c> integration event (Content hides their actions).</summary>
internal sealed record UserBlockedDomainEvent(Ulid UserId, Ulid BlockedBy, DateTimeOffset OccurredOnUtc)
    : IDomainEvent;
