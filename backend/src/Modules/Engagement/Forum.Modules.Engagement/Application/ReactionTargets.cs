using Forum.Modules.Engagement.Domain.Reactions;

namespace Forum.Modules.Engagement.Application;

/// <summary>Wire ↔ domain mapping for reaction target types (the wire names match the DB text values).</summary>
internal static class ReactionTargets
{
    public static bool TryParse(string? value, out ReactionTargetType target)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "thread":
                target = ReactionTargetType.Thread;
                return true;
            case "comment":
                target = ReactionTargetType.Comment;
                return true;
            default:
                target = default;
                return false;
        }
    }

    public static string ToWire(ReactionTargetType target) => target switch
    {
        ReactionTargetType.Thread => "thread",
        ReactionTargetType.Comment => "comment",
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown target type."),
    };
}
