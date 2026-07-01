using Forum.SharedKernel.Domain;

namespace Forum.Modules.Identity.Domain.Tokens;

/// <summary>
/// One link in a refresh-token rotation chain. Only the SHA-256 <see cref="TokenHash"/> is stored — never the opaque
/// value. All tokens minted from a single login share a <see cref="FamilyId"/> so reuse of a rotated/revoked token
/// can revoke the whole family (theft detection).
/// </summary>
internal sealed class RefreshToken : Entity<Ulid>
{
    // EF materialization.
    private RefreshToken()
    {
    }

    private RefreshToken(
        Ulid id, Ulid userId, Ulid familyId, string tokenHash,
        DateTimeOffset expiresOnUtc, DateTimeOffset createdOnUtc, string? ip, string? userAgent)
        : base(id)
    {
        UserId = userId;
        FamilyId = familyId;
        TokenHash = tokenHash;
        Status = RefreshTokenStatus.Active;
        ExpiresOnUtc = expiresOnUtc;
        CreatedOnUtc = createdOnUtc;
        Ip = ip;
        UserAgent = userAgent;
    }

    public Ulid UserId { get; private set; }

    public Ulid FamilyId { get; private set; }

    public string TokenHash { get; private set; } = default!;

    public RefreshTokenStatus Status { get; private set; }

    public DateTimeOffset ExpiresOnUtc { get; private set; }

    public DateTimeOffset CreatedOnUtc { get; private set; }

    /// <summary>The token this one was rotated into (next link in the chain), if any.</summary>
    public Ulid? RotatedTo { get; private set; }

    public string? Ip { get; private set; }

    public string? UserAgent { get; private set; }

    /// <summary>Mints the first token of a new family (a fresh login).</summary>
    public static RefreshToken IssueNew(
        Ulid userId, string tokenHash, DateTimeOffset expiresOnUtc, DateTimeOffset createdOnUtc, string? ip, string? userAgent) =>
        new(Ulid.NewUlid(), userId, Ulid.NewUlid(), tokenHash, expiresOnUtc, createdOnUtc, ip, userAgent);

    /// <summary>Mints the next token in this token's family (rotation on refresh).</summary>
    public RefreshToken IssueNextInFamily(
        string tokenHash, DateTimeOffset expiresOnUtc, DateTimeOffset createdOnUtc, string? ip, string? userAgent) =>
        new(Ulid.NewUlid(), UserId, FamilyId, tokenHash, expiresOnUtc, createdOnUtc, ip, userAgent);

    public bool IsActive(DateTimeOffset now) => Status == RefreshTokenStatus.Active && ExpiresOnUtc > now;

    /// <summary>Marks this token rotated and links it to its successor.</summary>
    public void Rotate(Ulid rotatedTo)
    {
        Status = RefreshTokenStatus.Rotated;
        RotatedTo = rotatedTo;
    }

    public void Revoke() => Status = RefreshTokenStatus.Revoked;
}
