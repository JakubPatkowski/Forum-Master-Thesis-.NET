namespace Forum.Modules.Identity.Application.Abstractions;

/// <summary>Hashes a plaintext password with Argon2id (ADR 0007), returning the PHC-encoded string.</summary>
internal interface IPasswordHasher
{
    string Hash(string password);
}
