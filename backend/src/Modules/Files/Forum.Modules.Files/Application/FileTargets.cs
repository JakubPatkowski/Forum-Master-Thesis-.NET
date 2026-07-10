using Forum.Modules.Content.Contracts;
using Forum.Modules.Files.Domain.Files;

namespace Forum.Modules.Files.Application;

/// <summary>Wire ↔ domain mapping for attachment target types (the wire names match the DB text values).</summary>
internal static class FileTargets
{
    public static bool TryParse(string? value, out FileTargetType target)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "thread":
                target = FileTargetType.Thread;
                return true;
            case "comment":
                target = FileTargetType.Comment;
                return true;
            case "category_icon":
                target = FileTargetType.CategoryIcon;
                return true;
            case "thread_icon":
                target = FileTargetType.ThreadIcon;
                return true;
            case "avatar":
                target = FileTargetType.Avatar;
                return true;
            case "dm":
                target = FileTargetType.Dm;
                return true;
            default:
                target = default;
                return false;
        }
    }

    public static string ToWire(FileTargetType target) => target switch
    {
        FileTargetType.Thread => "thread",
        FileTargetType.Comment => "comment",
        FileTargetType.CategoryIcon => "category_icon",
        FileTargetType.ThreadIcon => "thread_icon",
        FileTargetType.Avatar => "avatar",
        FileTargetType.Dm => "dm",
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown target type."),
    };

    /// <summary>The Content-owned targets map onto Content's authorization contract; avatar/dm do not.</summary>
    public static ContentAttachmentTarget? ToContentTarget(FileTargetType target) => target switch
    {
        FileTargetType.Thread => ContentAttachmentTarget.Thread,
        FileTargetType.Comment => ContentAttachmentTarget.Comment,
        FileTargetType.CategoryIcon => ContentAttachmentTarget.CategoryIcon,
        FileTargetType.ThreadIcon => ContentAttachmentTarget.ThreadIcon,
        _ => null,
    };
}
