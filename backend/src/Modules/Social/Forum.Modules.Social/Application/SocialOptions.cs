namespace Forum.Modules.Social.Application;

/// <summary>Module-local knobs, bound from the "Social" configuration section (Files' FilesOptions precedent).</summary>
internal sealed class SocialOptions
{
    public const string SectionName = "Social";

    /// <summary>A heartbeat younger than this reads as online.</summary>
    public int PresenceOnlineSeconds { get; set; } = 60;

    /// <summary>A heartbeat younger than this (but past online) reads as away; older (or absent) is offline.</summary>
    public int PresenceAwaySeconds { get; set; } = 300;

    /// <summary>Hard cap for one batch presence lookup.</summary>
    public int MaxPresenceBatch { get; set; } = 100;

    /// <summary>Default/maximum page sizes for the module's keyset lists.</summary>
    public int DefaultPageSize { get; set; } = 30;

    public int MaxPageSize { get; set; } = 100;

    /// <summary>Conversation lists are not keyset-paged (last-activity order is unstable); they are hard-capped.</summary>
    public int MaxConversationsListed { get; set; } = 200;
}
