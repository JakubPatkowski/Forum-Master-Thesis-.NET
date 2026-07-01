using Forum.Modules.Identity.Domain.Users;

namespace Forum.Modules.Identity.Application.Abstractions;

/// <summary>Write-side port for the <see cref="User"/> aggregate.</summary>
internal interface IUserRepository
{
    Task<User?> GetByIdAsync(Ulid id, CancellationToken cancellationToken);

    /// <summary>Looks up a user by email (case-insensitive via the <c>citext</c> column). Tracked for mutation.</summary>
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken);

    Task<bool> UsernameExistsAsync(string usernameLc, CancellationToken cancellationToken);

    Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken);

    void Add(User user);
}
