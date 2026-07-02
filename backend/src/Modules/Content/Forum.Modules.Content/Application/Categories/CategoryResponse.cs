namespace Forum.Modules.Content.Application.Categories;

internal sealed record CategoryResponse(
    Ulid Id,
    string Slug,
    string Name,
    string Description,
    string Visibility,
    Ulid OwnerId,
    Ulid? IconFileId,
    DateTimeOffset CreatedOnUtc);
