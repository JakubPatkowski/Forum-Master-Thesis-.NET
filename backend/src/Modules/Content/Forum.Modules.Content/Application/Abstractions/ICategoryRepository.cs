using Forum.Modules.Content.Domain.Categories;

namespace Forum.Modules.Content.Application.Abstractions;

internal interface ICategoryRepository
{
    /// <summary>Tracked load for writes; the soft-delete filter hides deleted categories.</summary>
    Task<Category?> GetByIdAsync(Ulid id, CancellationToken cancellationToken);

    /// <summary>Tracked load for writes; the soft-delete filter hides deleted categories.</summary>
    Task<Category?> GetBySlugAsync(string slug, CancellationToken cancellationToken);

    /// <summary>True when any category (including soft-deleted) already claims the slug.</summary>
    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken);

    void Add(Category category);
}
