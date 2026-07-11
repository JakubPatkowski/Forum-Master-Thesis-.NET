using Forum.Modules.Identity.Domain.Users.Events;
using Forum.SharedKernel.Domain;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Identity.Domain.Users;

/// <summary>
/// A forum account. The only mutation entry point for identity state: registration, blocking and password rehash.
/// Audit columns are stamped by the interceptor; status drives whether the account can act.
/// </summary>
internal sealed class User : AggregateRoot<Ulid>
{
    // EF materialization.
    private User()
    {
    }

    private User(Ulid id, string username, string usernameLc, string email, string displayName, string passwordHash)
        : base(id)
    {
        Username = username;
        UsernameLc = usernameLc;
        Email = email;
        DisplayName = displayName;
        PasswordHash = passwordHash;
        Status = UserStatus.Active;
    }

    public string Username { get; private set; } = default!;

    /// <summary>Lower-cased username for case-insensitive uniqueness.</summary>
    public string UsernameLc { get; private set; } = default!;

    public string Email { get; private set; } = default!;

    public string DisplayName { get; private set; } = default!;

    /// <summary>Argon2id PHC-encoded string (ADR 0007).</summary>
    public string PasswordHash { get; private set; } = default!;

    public UserStatus Status { get; private set; }

    /// <summary>Logical reference to <c>forum_files.files</c> (no cross-schema FK).</summary>
    public Ulid? AvatarFileId { get; private set; }

    public bool IsActive => Status == UserStatus.Active;

    /// <summary>Creates a new active account and raises <see cref="UserRegisteredDomainEvent"/>. Inputs are pre-validated at the edge.</summary>
    public static User Register(string username, string email, string displayName, string passwordHash)
    {
        var normalizedUsername = username.Trim();
        var user = new User(
            Ulid.NewUlid(),
            normalizedUsername,
            normalizedUsername.ToLowerInvariant(),
            email.Trim(),
            displayName.Trim(),
            passwordHash);

        user.Raise(new UserRegisteredDomainEvent(user.Id, user.Username, user.Email, DateTimeOffset.UtcNow));
        return user;
    }

    /// <summary>
    /// Constructs an account directly for the offline seeder (Phase 9b): explicit deterministic id, pre-set audit
    /// timestamp, no domain event raised. <c>created_by</c> is null, mirroring a real (anonymous) self-registration.
    /// </summary>
    internal static User Seed(
        Ulid id, string username, string email, string displayName, string passwordHash,
        UserStatus status, DateTimeOffset createdOnUtc)
    {
        var normalizedUsername = username.Trim();
        var user = new User(
            id, normalizedUsername, normalizedUsername.ToLowerInvariant(), email.Trim(), displayName.Trim(), passwordHash)
        {
            Status = status,
        };
        user.SetCreated(createdOnUtc, by: null);
        return user;
    }

    /// <summary>Blocks the account and raises <see cref="UserBlockedDomainEvent"/>. No-op failure if already blocked.</summary>
    public Result Block(Ulid by)
    {
        if (Status == UserStatus.Blocked)
        {
            return Result.Failure(UserErrors.AlreadyBlocked);
        }

        Status = UserStatus.Blocked;
        Raise(new UserBlockedDomainEvent(Id, by, DateTimeOffset.UtcNow));
        return Result.Success();
    }

    /// <summary>Re-activates a blocked account.</summary>
    public Result Unblock()
    {
        if (Status != UserStatus.Blocked)
        {
            return Result.Failure(UserErrors.NotBlocked);
        }

        Status = UserStatus.Active;
        return Result.Success();
    }

    /// <summary>Replaces the stored hash (rehash-on-login when Argon2 parameters are raised).</summary>
    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;

    /// <summary>Renames the account. Uniqueness of the lower-cased form is checked by the caller.</summary>
    public void ChangeUsername(string username)
    {
        var normalized = username.Trim();
        Username = normalized;
        UsernameLc = normalized.ToLowerInvariant();
    }

    /// <summary>Replaces the account email. Uniqueness (citext) is checked by the caller.</summary>
    public void ChangeEmail(string email) => Email = email.Trim();
}
