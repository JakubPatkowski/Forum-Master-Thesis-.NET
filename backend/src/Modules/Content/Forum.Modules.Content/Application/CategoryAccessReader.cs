using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Contracts;
using Forum.Modules.Content.Domain.Categories;

namespace Forum.Modules.Content.Application;

/// <summary>
/// Implements <see cref="IContentVisibility"/> from the Category aggregate: the repository's soft-delete filter
/// makes a deleted category resolve to null, so callers drop pushes for vanished categories for free.
/// </summary>
internal sealed class CategoryAccessReader : IContentVisibility
{
    private readonly ICategoryRepository _categories;

    public CategoryAccessReader(ICategoryRepository categories) => _categories = categories;

    public async Task<CategoryAccess?> GetCategoryAccessAsync(Ulid categoryId, CancellationToken cancellationToken)
    {
        var category = await _categories.GetByIdAsync(categoryId, cancellationToken);
        return category is null
            ? null
            : new CategoryAccess(category.Id, category.OwnerId, category.Visibility == Visibility.Private);
    }
}
