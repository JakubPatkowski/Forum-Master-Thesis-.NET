namespace Forum.Modules.Content.Application.Abstractions;

/// <summary>Commits the Content module's pending writes and dispatches the raised domain events.</summary>
internal interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
