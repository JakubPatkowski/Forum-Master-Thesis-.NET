using Forum.Common.Security;
using Forum.SharedKernel.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Forum.Infrastructure.Persistence;

/// <summary>Stamps created/last-modified audit columns on aggregates whenever changes are saved.</summary>
public sealed class AuditInterceptor : SaveChangesInterceptor
{
    private readonly TimeProvider _clock;
    private readonly ICurrentActor _actor;

    public AuditInterceptor(TimeProvider clock, ICurrentActor actor)
    {
        _clock = clock;
        _actor = actor;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        var now = _clock.GetUtcNow();
        var actor = _actor.Id;

        foreach (var entry in context.ChangeTracker.Entries<IAuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.SetCreated(now, actor);
                    break;
                case EntityState.Modified:
                    entry.Entity.SetModified(now, actor);
                    break;
                default:
                    break;
            }
        }
    }
}
