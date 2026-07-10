using Forum.SharedKernel.Results;

namespace Forum.Modules.Identity.Application.Account;

/// <summary>Errors for self-service account changes.</summary>
internal static class AccountErrors
{
    /// <summary>
    /// The password confirmation on a settings change failed. Deliberately a 422, not a 401 —
    /// the session itself is valid, so the client must not try to refresh tokens over this.
    /// </summary>
    public static readonly Error InvalidPassword =
        Error.Validation("account.invalid_password", "Current password is incorrect.");
}
