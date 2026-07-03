namespace Forum.Modules.Files.Application.Abstractions;

/// <summary>Commits the module's tracked changes (and dispatches raised domain events) atomically.</summary>
internal interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
