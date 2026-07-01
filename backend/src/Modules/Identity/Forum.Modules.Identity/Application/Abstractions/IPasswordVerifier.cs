namespace Forum.Modules.Identity.Application.Abstractions;

/// <summary>Constant-time Argon2id verification, with a dummy verify to keep login timing independent of account existence.</summary>
internal interface IPasswordVerifier
{
    bool Verify(string passwordHash, string password);

    /// <summary>Performs a verify against a fixed dummy hash so a "user not found" path costs the same as a real verify.</summary>
    void VerifyDummy(string password);
}
