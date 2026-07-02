using Forum.Modules.Content.Domain.Categories;

namespace Forum.Modules.Content.Presentation;

/// <summary>Maps the wire form of visibility ("public"/"private", default public) to the domain enum.</summary>
internal static class VisibilityRequest
{
    /// <summary>Null when the value is not a recognized visibility.</summary>
    public static Visibility? Parse(string? value) => value switch
    {
        null or "" or "public" => Visibility.Public,
        "private" => Visibility.Private,
        _ => null,
    };
}
