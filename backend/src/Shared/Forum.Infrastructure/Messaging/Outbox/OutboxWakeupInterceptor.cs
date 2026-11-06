using System.Data.Common;
using System.Runtime.CompilerServices;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace Forum.Infrastructure.Messaging.Outbox;

/// <summary>
/// Wakes the module's outbox relay after a transaction that inserted <see cref="OutboxMessage"/> rows has
/// <b>committed</b> — not merely saved. A plain <c>SaveChanges</c> commits its own transaction before it returns,
/// so the signal fires from <c>SavedChanges</c>; inside an explicit transaction (the consumer host wraps the inbox
/// row and the handlers' writes in one) the rows are invisible until <c>Commit</c>, so the signal is deferred to
/// <c>TransactionCommitted</c> and dropped on a rollback. State is kept per DbContext instance (the interceptor is a
/// singleton shared by every module context). Disabled by <c>Messaging:WakeRelayOnWrite=false</c>, which restores
/// pure polling.
/// </summary>
public sealed class OutboxWakeupInterceptor : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private readonly ConditionalWeakTable<DbContext, StrongBox<bool>> _pendingByContext = new();
    private readonly OutboxRelayWakeup _wakeup;
    private readonly bool _enabled;

    public OutboxWakeupInterceptor(OutboxRelayWakeup wakeup, IOptions<MessagingOptions> options)
    {
        _wakeup = wakeup;
        _enabled = options.Value.WakeRelayOnWrite;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        NoteOutboxInserts(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        NoteOutboxInserts(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        SignalIfCommitted(eventData.Context);
        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        SignalIfCommitted(eventData.Context);
        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        Forget(eventData.Context);
        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData.Context);
        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    void IDbTransactionInterceptor.TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData) =>
        SignalPending(eventData.Context);

    Task IDbTransactionInterceptor.TransactionCommittedAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken)
    {
        SignalPending(eventData.Context);
        return Task.CompletedTask;
    }

    void IDbTransactionInterceptor.TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) =>
        Forget(eventData.Context);

    Task IDbTransactionInterceptor.TransactionRolledBackAsync(
        DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken)
    {
        Forget(eventData.Context);
        return Task.CompletedTask;
    }

    private void NoteOutboxInserts(DbContext? context)
    {
        if (!_enabled || context is null)
        {
            return;
        }

        foreach (var entry in context.ChangeTracker.Entries<OutboxMessage>())
        {
            if (entry.State == EntityState.Added)
            {
                _pendingByContext.GetOrCreateValue(context).Value = true;
                return;
            }
        }
    }

    /// <summary>After SaveChanges: its implicit transaction is already committed; an explicit one waits for Commit.</summary>
    private void SignalIfCommitted(DbContext? context)
    {
        if (context is not null && context.Database.CurrentTransaction is null)
        {
            SignalPending(context);
        }
    }

    private void SignalPending(DbContext? context)
    {
        if (context is null || !_pendingByContext.TryGetValue(context, out var pending) || !pending.Value)
        {
            return;
        }

        pending.Value = false;
        _wakeup.For(context.GetType()).Set();
    }

    private void Forget(DbContext? context)
    {
        if (context is not null && _pendingByContext.TryGetValue(context, out var pending))
        {
            pending.Value = false;
        }
    }
}
