using Forum.Modules.Identity.Domain.Tokens;

namespace Forum.Modules.Identity.Application.Abstractions;

/// <summary>Write-side port for refresh tokens (lookup by hash, family/user revocation).</summary>
internal interface IRefreshTokenRepository
{
    /// <summary>Finds a token by its SHA-256 hash. Tracked so it can be rotated/revoked.</summary>
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken);

    void Add(RefreshToken token);

    /// <summary>Revokes every active token in a family (reuse/theft detection).</summary>
    Task RevokeFamilyAsync(Ulid familyId, CancellationToken cancellationToken);

    /// <summary>Revokes every active token for a user (logout-all).</summary>
    Task RevokeAllForUserAsync(Ulid userId, CancellationToken cancellationToken);
}
