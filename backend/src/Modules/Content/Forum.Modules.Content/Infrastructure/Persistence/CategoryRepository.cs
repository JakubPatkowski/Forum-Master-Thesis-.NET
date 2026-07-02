using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Domain.Categories;

using Microsoft.EntityFrameworkCore;

namespace Forum.Modules.Content.Infrastructure.Persistence;

internal sealed class CategoryRepository : ICategoryRepository
{
    private readonly ContentDbContext _db;

    public CategoryRepository(ContentDbContext db) => _db = db;

    public Task<Category?> GetByIdAsync(Ulid id, CancellationToken cancellationToken) =>
        _db.Categories.AsTracking().FirstOrDefaultAsync(category => category.Id == id, cancellationToken);

    public Task<Category?> GetBySlugAsync(string slug, CancellationToken cancellationToken) =>
        _db.Categories.AsTracking().FirstOrDefaultAsync(category => category.Slug == slug, cancellationToken);

    // IgnoreQueryFilters: a soft-deleted category still owns its slug (the unique index spans all rows).
    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        _db.Categories.IgnoreQueryFilters().AnyAsync(category => category.Slug == slug, cancellationToken);

    public void Add(Category category) => _db.Categories.Add(category);
}
