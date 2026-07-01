namespace Forum.Modules.Identity.Domain.Users;

/// <summary>Account lifecycle state. Persisted as the snake_case labels <c>active</c>/<c>blocked</c>/<c>pending_verification</c>.</summary>
internal enum UserStatus
{
    Active = 0,
    Blocked = 1,
    PendingVerification = 2,
}
