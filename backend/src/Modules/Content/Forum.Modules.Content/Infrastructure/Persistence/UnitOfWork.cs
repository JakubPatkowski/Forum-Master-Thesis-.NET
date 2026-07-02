using Forum.Modules.Content.Application.Abstractions;

namespace Forum.Modules.Content.Infrastructure.Persistence;

internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly ContentDbContext _db;

    public UnitOfWork(ContentDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAndDispatchEventsAsync(cancellationToken);
}
