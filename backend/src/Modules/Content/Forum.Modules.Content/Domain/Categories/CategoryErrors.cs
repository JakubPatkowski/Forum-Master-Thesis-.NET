using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Domain.Categories;

/// <summary>Typed errors for the category lifecycle. No exceptions for expected failures.</summary>
internal static class CategoryErrors
{
    public static readonly Error NotFound = Error.NotFound("category.not_found", "Category not found.");
    public static readonly Error SlugTaken = Error.Conflict("category.slug_taken", "Category slug is already taken.");
    public static readonly Error AlreadyDeleted = Error.Conflict("category.already_deleted", "Category is already deleted.");
    public static readonly Error NotOwnerNorModerator = Error.Forbidden(
        "category.forbidden", "Only the owner or a moderator may modify this category.");
    public static readonly Error PrivateCategory = Error.Forbidden(
        "category.private", "This category only accepts threads from its owner or moderators.");
}
