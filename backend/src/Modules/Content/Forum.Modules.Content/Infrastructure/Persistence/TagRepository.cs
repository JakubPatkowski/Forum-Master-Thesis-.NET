using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Domain.Threads;

using Microsoft.EntityFrameworkCore;

namespace Forum.Modules.Content.Infrastructure.Persistence;

internal sealed class TagRepository : ITagRepository
{
    private readonly ContentDbContext _db;

    public TagRepository(ContentDbContext db) => _db = db;

    public async Task<IReadOnlyList<Tag>> GetBySlugsAsync(
        IReadOnlyCollection<string> slugs, CancellationToken cancellationToken) =>
        await _db.Tags.Where(tag => slugs.Contains(tag.Slug)).ToListAsync(cancellationToken);

    public void Add(Tag tag) => _db.Tags.Add(tag);
}
