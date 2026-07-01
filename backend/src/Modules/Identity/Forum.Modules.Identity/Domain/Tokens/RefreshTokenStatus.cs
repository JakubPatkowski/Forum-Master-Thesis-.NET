namespace Forum.Modules.Identity.Domain.Tokens;

/// <summary>Refresh-token state in the rotation chain. Persisted as <c>active</c>/<c>rotated</c>/<c>revoked</c>.</summary>
internal enum RefreshTokenStatus
{
    Active = 0,
    Rotated = 1,
    Revoked = 2,
}
