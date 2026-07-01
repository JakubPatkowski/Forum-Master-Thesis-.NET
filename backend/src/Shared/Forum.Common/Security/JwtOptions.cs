using System.Text;

namespace Forum.Common.Security;

/// <summary>
/// JWT bearer settings (bound from the <c>Jwt</c> configuration section). The signing key comes from a secret in
/// real environments; when absent a clearly-marked development key is used so the host always boots for local/test runs.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    // Dev-only fallback so `dotnet run` and tests work without provisioning a secret. NEVER used when Jwt:SigningKey is set
    // (the cluster supplies it via a k8s Secret). 32+ bytes for HS256.
    internal const string DevelopmentSigningKey = "forum-dotnet-dev-signing-key-do-not-use-in-production-0123456789";

    public string Issuer { get; set; } = "forum-dotnet";

    public string Audience { get; set; } = "forum-dotnet";

    /// <summary>HMAC-SHA256 signing key. Left null in source/appsettings; provided via secret in dev and cluster.</summary>
    public string? SigningKey { get; set; }

    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 14;

    /// <summary>The effective signing key bytes: the configured key, or the development fallback when none is set.</summary>
    public byte[] SigningKeyBytes() =>
        Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(SigningKey) ? DevelopmentSigningKey : SigningKey);
}
