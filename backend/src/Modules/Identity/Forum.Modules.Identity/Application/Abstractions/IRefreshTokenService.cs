namespace Forum.Modules.Identity.Application.Abstractions;

/// <summary>Generates opaque refresh tokens and hashes them (only the hash is ever stored).</summary>
internal interface IRefreshTokenService
{
    /// <summary>Cryptographically-random opaque token handed to the client (httpOnly cookie).</summary>
    string Generate();

    /// <summary>SHA-256 of the opaque token, for storage and lookup.</summary>
    string Hash(string token);
}
