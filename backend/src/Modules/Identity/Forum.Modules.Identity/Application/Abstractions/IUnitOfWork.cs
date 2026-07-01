namespace Forum.Modules.Identity.Application.Abstractions;

/// <summary>Commits the tracked changes (aggregates + outbox rows) atomically and dispatches domain events.</summary>
internal interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
