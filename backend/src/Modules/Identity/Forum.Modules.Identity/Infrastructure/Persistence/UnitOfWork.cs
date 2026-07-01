using Forum.Modules.Identity.Application.Abstractions;

namespace Forum.Modules.Identity.Infrastructure.Persistence;

internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly IdentityDbContext _db;

    public UnitOfWork(IdentityDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAndDispatchEventsAsync(cancellationToken);
}
