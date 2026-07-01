using Forum.Modules.Identity.Application.Abstractions;
using Forum.Modules.Identity.Domain.Tokens;

using Microsoft.EntityFrameworkCore;

namespace Forum.Modules.Identity.Infrastructure.Persistence;

internal sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly IdentityDbContext _db;

    public RefreshTokenRepository(IdentityDbContext db) => _db = db;

    public Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        _db.RefreshTokens.AsTracking().FirstOrDefaultAsync(token => token.TokenHash == tokenHash, cancellationToken);

    public void Add(RefreshToken token) => _db.RefreshTokens.Add(token);

    public Task RevokeFamilyAsync(Ulid familyId, CancellationToken cancellationToken) =>
        _db.RefreshTokens
            .Where(token => token.FamilyId == familyId && token.Status == RefreshTokenStatus.Active)
            .ExecuteUpdateAsync(set => set.SetProperty(token => token.Status, RefreshTokenStatus.Revoked), cancellationToken);

    public Task RevokeAllForUserAsync(Ulid userId, CancellationToken cancellationToken) =>
        _db.RefreshTokens
            .Where(token => token.UserId == userId && token.Status == RefreshTokenStatus.Active)
            .ExecuteUpdateAsync(set => set.SetProperty(token => token.Status, RefreshTokenStatus.Revoked), cancellationToken);
}
