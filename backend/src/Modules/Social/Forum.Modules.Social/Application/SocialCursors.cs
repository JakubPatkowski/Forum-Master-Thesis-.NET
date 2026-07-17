using System.Globalization;

namespace Forum.Modules.Social.Application;

/// <summary>
/// The module's keyset cursor convention: descending ULID-id order everywhere, so the cursor IS the last row's
/// ULID (already opaque-ish, always validated). Simpler than Content's multi-field Base64Url cursors because no
/// social list orders by anything but creation time.
/// </summary>
internal static class SocialCursors
{
    /// <summary>Null/empty parses to "first page" (null); anything else must be a valid ULID.</summary>
    public static bool TryParse(string? cursor, out Ulid? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return true;
        }

        if (!Ulid.TryParse(cursor, CultureInfo.InvariantCulture, out var ulid))
        {
            return false;
        }

        parsed = ulid;
        return true;
    }

    public static int ClampLimit(int? limit, SocialOptions options) =>
        limit is null or <= 0 ? options.DefaultPageSize : Math.Min(limit.Value, options.MaxPageSize);
}
