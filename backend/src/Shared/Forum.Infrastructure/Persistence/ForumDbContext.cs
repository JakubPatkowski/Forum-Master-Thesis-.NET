using System.Linq.Expressions;

using Forum.Infrastructure.Messaging;
using Forum.SharedKernel.Domain;

using Microsoft.EntityFrameworkCore;

namespace Forum.Infrastructure.Persistence;

/// <summary>
/// Base DbContext every module inherits. Provides no-tracking reads by default, a global soft-delete
/// query filter, and an atomic "save then dispatch domain events" entry point. snake_case mapping is
/// applied where the context is registered (see <see cref="ModuleDbContextRegistration"/>).
/// </summary>
public abstract class ForumDbContext : DbContext
{
    private readonly IDomainEventDispatcher _dispatcher;

    protected ForumDbContext(DbContextOptions options, IDomainEventDispatcher dispatcher) : base(options)
    {
        _dispatcher = dispatcher;
        ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ApplySoftDeleteQueryFilter(modelBuilder);
    }

    /// <summary>Persists the tracked changes, then dispatches the domain events raised by the touched aggregates.</summary>
    public async Task<int> SaveChangesAndDispatchEventsAsync(CancellationToken cancellationToken = default)
    {
        var aggregates = ChangeTracker.Entries<IHasDomainEvents>()
            .Select(static entry => entry.Entity)
            .Where(static aggregate => aggregate.DomainEvents.Count > 0)
            .ToArray();

        var domainEvents = aggregates.SelectMany(static aggregate => aggregate.DomainEvents).ToArray();

        var affected = await SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        foreach (var aggregate in aggregates)
        {
            aggregate.ClearDomainEvents();
        }

        await _dispatcher.DispatchAsync(domainEvents, cancellationToken).ConfigureAwait(false);
        return affected;
    }

    private static void ApplySoftDeleteQueryFilter(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(ISoftDeletable).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var isDeleted = Expression.Property(parameter, nameof(ISoftDeletable.IsDeleted));
            var notDeleted = Expression.Lambda(Expression.Not(isDeleted), parameter);
            modelBuilder.Entity(entityType.ClrType).HasQueryFilter(notDeleted);
        }
    }
}
