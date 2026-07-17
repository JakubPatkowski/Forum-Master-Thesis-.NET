using Forum.Modules.Social.Domain.Groups;

namespace Forum.Modules.Social.Application.Groups;

/// <summary>Wire ↔ domain mapping for group enums (wire names match the DB text values).</summary>
internal static class GroupWire
{
    public static bool TryParseVisibility(string? value, out GroupVisibility visibility)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "public":
                visibility = GroupVisibility.Public;
                return true;
            case "private":
                visibility = GroupVisibility.Private;
                return true;
            default:
                visibility = default;
                return false;
        }
    }

    public const string AdminRole = "admin";
    public const string MemberRole = "member";
}
