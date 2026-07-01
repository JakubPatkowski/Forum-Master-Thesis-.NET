using System.Security.Cryptography;
using System.Text;

using Forum.Modules.Identity.Application.Abstractions;

namespace Forum.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Opaque refresh tokens: 256 bits of randomness handed to the client; only the SHA-256 hash is stored and looked up.
/// SHA-256 (not Argon2) is correct here — the token is high-entropy and not a low-entropy password.
/// </summary>
internal sealed class RefreshTokenService : IRefreshTokenService
{
    private const int TokenBytes = 32;

    public string Generate()
    {
        Span<byte> bytes = stackalloc byte[TokenBytes];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexStringLower(bytes);
    }

    public string Hash(string token)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexStringLower(digest);
    }
}
