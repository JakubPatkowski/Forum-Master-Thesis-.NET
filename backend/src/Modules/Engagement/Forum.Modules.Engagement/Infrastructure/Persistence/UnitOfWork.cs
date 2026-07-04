using Forum.Modules.Engagement.Application.Abstractions;

namespace Forum.Modules.Engagement.Infrastructure.Persistence;

internal sealed class UnitOfWork : IUnitOfWork
{
    private readonly EngagementDbContext _db;

    public UnitOfWork(EngagementDbContext db) => _db = db;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _db.SaveChangesAndDispatchEventsAsync(cancellationToken);
}
