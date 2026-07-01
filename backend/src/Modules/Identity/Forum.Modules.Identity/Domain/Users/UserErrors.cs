using Forum.SharedKernel.Results;

namespace Forum.Modules.Identity.Domain.Users;

/// <summary>Typed errors for the user lifecycle. No exceptions for expected failures.</summary>
internal static class UserErrors
{
    public static readonly Error NotFound = Error.NotFound("user.not_found", "User not found.");
    public static readonly Error UsernameTaken = Error.Conflict("user.username_taken", "Username is already taken.");
    public static readonly Error EmailTaken = Error.Conflict("user.email_taken", "Email is already registered.");
    public static readonly Error AlreadyBlocked = Error.Conflict("user.already_blocked", "User is already blocked.");
    public static readonly Error NotBlocked = Error.Conflict("user.not_blocked", "User is not blocked.");
}
