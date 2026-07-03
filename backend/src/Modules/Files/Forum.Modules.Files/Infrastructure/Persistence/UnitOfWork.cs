using Forum.Modules.Files.Application.Abstractions;

namespace Forum.Modules.Files.Infrastructure.Persistence;

internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly FilesDbContext _db;

    public UnitOfWork(FilesDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAndDispatchEventsAsync(cancellationToken);
}
