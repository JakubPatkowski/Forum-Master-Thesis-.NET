using Forum.Modules.Social.Domain.Privacy;

namespace Forum.Modules.Social.Application.Privacy;

/// <summary>Wire ↔ domain mapping for privacy audiences (wire names match the DB text values).</summary>
internal static class PrivacyWire
{
    public static bool TryParse(string? value, out PrivacyAudience audience)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "everyone":
                audience = PrivacyAudience.Everyone;
                return true;
            case "friends":
                audience = PrivacyAudience.Friends;
                return true;
            case "no_one":
                audience = PrivacyAudience.NoOne;
                return true;
            default:
                audience = default;
                return false;
        }
    }

    public static string ToWire(PrivacyAudience audience) => audience switch
    {
        PrivacyAudience.Everyone => "everyone",
        PrivacyAudience.Friends => "friends",
        PrivacyAudience.NoOne => "no_one",
        _ => throw new ArgumentOutOfRangeException(nameof(audience), audience, "Unknown audience."),
    };
}
