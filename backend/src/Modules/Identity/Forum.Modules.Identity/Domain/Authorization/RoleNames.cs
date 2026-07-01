namespace Forum.Modules.Identity.Domain.Authorization;

/// <summary>The seeded global role names (<c>user &lt; moderator &lt; admin</c>). Mirrors the <c>forum_authz.roles</c> seed.</summary>
internal static class RoleNames
{
    public const string User = "user";
    public const string Moderator = "moderator";
    public const string Admin = "admin";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        User,
        Moderator,
        Admin,
    };
}
