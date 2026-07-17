using Forum.Modules.Content.Contracts;
using Forum.Modules.Files.Domain.Files;
using Forum.Modules.Social.Contracts;

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
            case "message":
                target = FileTargetType.Message;
                return true;
            case "group_icon":
                target = FileTargetType.GroupIcon;
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
        FileTargetType.Message => "message",
        FileTargetType.GroupIcon => "group_icon",
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown target type."),
    };

    /// <summary>The Content-owned targets map onto Content's authorization contract; the rest do not.</summary>
    public static ContentAttachmentTarget? ToContentTarget(FileTargetType target) => target switch
    {
        FileTargetType.Thread => ContentAttachmentTarget.Thread,
        FileTargetType.Comment => ContentAttachmentTarget.Comment,
        FileTargetType.CategoryIcon => ContentAttachmentTarget.CategoryIcon,
        FileTargetType.ThreadIcon => ContentAttachmentTarget.ThreadIcon,
        _ => null,
    };

    /// <summary>The Social-owned targets map onto Social's authorization contract; the rest do not.</summary>
    public static SocialAttachmentTarget? ToSocialTarget(FileTargetType target) => target switch
    {
        FileTargetType.Message => SocialAttachmentTarget.Message,
        FileTargetType.GroupIcon => SocialAttachmentTarget.GroupIcon,
        _ => null,
    };
}
