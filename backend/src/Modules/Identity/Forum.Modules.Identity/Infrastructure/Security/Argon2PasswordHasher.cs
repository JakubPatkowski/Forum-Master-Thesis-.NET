using Forum.Modules.Identity.Application.Abstractions;

using Isopoh.Cryptography.Argon2;

namespace Forum.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Argon2id password hashing (ADR 0007). Produces a PHC-encoded string carrying its own parameters, verifies in
/// constant time, and offers a dummy verify so a "user not found" login costs the same as a real one.
/// </summary>
internal sealed class Argon2PasswordHasher : IPasswordHasher, IPasswordVerifier
{
    // OWASP baseline: m = 19 MiB, t = 2, p = 1 (tunable; the cost is encoded in the hash for later rehash).
    private const int MemoryCostKib = 19 * 1024;
    private const int TimeCost = 2;
    private const int Parallelism = 1;

    // A real Argon2id hash used only to spend comparable time when the account does not exist.
    private static readonly string DummyHash = HashInternal("dummy-password-to-equalize-timing");

    public string Hash(string password) => HashInternal(password);

    public bool Verify(string passwordHash, string password) => Argon2.Verify(passwordHash, password);

    public void VerifyDummy(string password) => Argon2.Verify(DummyHash, password);

    private static string HashInternal(string password) =>
        Argon2.Hash(password, TimeCost, MemoryCostKib, Parallelism, Argon2Type.HybridAddressing);
}
