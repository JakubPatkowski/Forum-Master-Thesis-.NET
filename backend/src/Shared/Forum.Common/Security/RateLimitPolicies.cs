namespace Forum.Common.Security;

/// <summary>Named rate-limit policies shared between the host (which defines them) and module endpoints (which apply them).</summary>
public static class RateLimitPolicies
{
    /// <summary>Tighter per-IP limit for authentication endpoints (login/register).</summary>
    public const string Auth = "auth";
}
