using Forum.SharedKernel.Domain;

namespace Forum.Modules.Identity.Domain.Users.Events;

/// <summary>Raised when a new account is created. Translated to the <c>UserRegistered</c> integration event for other modules.</summary>
internal sealed record UserRegisteredDomainEvent(Ulid UserId, string Username, string Email, DateTimeOffset OccurredOnUtc)
    : IDomainEvent;
