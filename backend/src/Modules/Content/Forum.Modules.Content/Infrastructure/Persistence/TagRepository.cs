using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Domain.Threads;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Forum.Modules.Content.Infrastructure.Persistence;

internal sealed class TagRepository : ITagRepository
{
    private readonly ContentDbContext _db;

    public TagRepository(ContentDbContext db) => _db = db;

    public async Task<IReadOnlyList<Tag>> GetBySlugsAsync(
        IReadOnlyCollection<string> slugs, CancellationToken cancellationToken) =>
        await _db.Tags.Where(tag => slugs.Contains(tag.Slug)).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Tag>> GetOrCreateAsync(
        IReadOnlyCollection<string> slugs, CancellationToken cancellationToken)
    {
        var existing = await GetBySlugsAsync(slugs, cancellationToken);
        var missing = slugs.Where(slug => existing.All(tag => tag.Slug != slug)).ToArray();
        if (missing.Length == 0)
        {
            return existing;
        }

        // INSERT … ON CONFLICT DO NOTHING on the unique slug index: of two requests racing to create the same new
        // slug, the loser's row is silently skipped and the re-read below returns the winner's. The statement runs
        // on the context's connection and autocommits ahead of the thread's unit of work — a tag is a shared,
        // never-deleted label, so a row that outlives a failed thread insert is harmless (it just exists early).
        var ids = missing.Select(static _ => Ulid.NewUlid().ToString()).ToArray();
        await _db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO forum_content.tags (id, slug, name)
            SELECT * FROM unnest(@ids::text[], @slugs::text[], @names::text[])
            ON CONFLICT (slug) DO NOTHING
            """,
            [new NpgsqlParameter("ids", ids), new NpgsqlParameter("slugs", missing), new NpgsqlParameter("names", missing)],
            cancellationToken);

        return await GetBySlugsAsync(slugs, cancellationToken);
    }
}
