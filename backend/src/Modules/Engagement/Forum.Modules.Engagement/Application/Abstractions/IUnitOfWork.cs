namespace Forum.Modules.Engagement.Application.Abstractions;

/// <summary>Commits the Engagement module's pending writes and dispatches the raised domain events.</summary>
internal interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
