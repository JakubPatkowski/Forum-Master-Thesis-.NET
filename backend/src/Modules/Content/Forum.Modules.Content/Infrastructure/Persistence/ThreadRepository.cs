using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Domain.Threads;

using Microsoft.EntityFrameworkCore;

namespace Forum.Modules.Content.Infrastructure.Persistence;

internal sealed class ThreadRepository : IThreadRepository
{
    private readonly ContentDbContext _db;

    public ThreadRepository(ContentDbContext db) => _db = db;

    public Task<Thread?> GetByIdAsync(Ulid id, CancellationToken cancellationToken) =>
        _db.Threads.AsTracking().FirstOrDefaultAsync(thread => thread.Id == id, cancellationToken);

    public void Add(Thread thread) => _db.Threads.Add(thread);

    public void AttachTags(Ulid threadId, IReadOnlyCollection<Ulid> tagIds) =>
        _db.ThreadTags.AddRange(tagIds.Select(tagId => new ThreadTag(threadId, tagId)));
}
