namespace Forum.Modules.Engagement.Domain.Reactions;

/// <summary>
/// The reaction-kind vocabulary. Only <see cref="Like"/> exists today; the column (and its slot in the
/// reactions primary key) is the schema hook that lets a later phase add more kinds — Discord-style
/// multiple reactions per user per target — without a breaking migration.
/// </summary>
internal static class ReactionTypes
{
    public const string Like = "like";
}
