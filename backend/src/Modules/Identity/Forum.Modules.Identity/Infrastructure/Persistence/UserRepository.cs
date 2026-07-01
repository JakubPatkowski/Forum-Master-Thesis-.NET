using Forum.Modules.Identity.Application.Abstractions;
using Forum.Modules.Identity.Domain.Users;

using Microsoft.EntityFrameworkCore;

namespace Forum.Modules.Identity.Infrastructure.Persistence;

internal sealed class UserRepository : IUserRepository
{
    private readonly IdentityDbContext _db;

    public UserRepository(IdentityDbContext db) => _db = db;

    public Task<User?> GetByIdAsync(Ulid id, CancellationToken cancellationToken) =>
        _db.Users.AsTracking().FirstOrDefaultAsync(user => user.Id == id, cancellationToken);

    public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken) =>
        _db.Users.AsTracking().FirstOrDefaultAsync(user => user.Email == email, cancellationToken);

    public Task<bool> UsernameExistsAsync(string usernameLc, CancellationToken cancellationToken) =>
        _db.Users.AnyAsync(user => user.UsernameLc == usernameLc, cancellationToken);

    public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken) =>
        _db.Users.AnyAsync(user => user.Email == email, cancellationToken);

    public void Add(User user) => _db.Users.Add(user);
}
