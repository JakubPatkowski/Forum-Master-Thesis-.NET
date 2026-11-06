using System.Collections.Concurrent;

namespace Forum.Infrastructure.Messaging.Outbox;

/// <summary>
/// The wake-up line between a module's request path and that module's outbox relay on the SAME replica (A/B v5,
/// A4). A relay sleeps a poll interval between passes (<c>Messaging:PollIntervalMilliseconds</c>, 5 s in the
/// cluster — the floor of every WebSocket push latency so far, see deviation D14); once a transaction that inserted
/// outbox rows has committed, <see cref="OutboxWakeupInterceptor"/> sets this signal and the relay claims at once.
/// The poll stays as the fallback for rows committed by other replicas and for anything a crash left behind.
/// One signal per DbContext type = per module relay.
/// </summary>
public sealed class OutboxRelayWakeup : IDisposable
{
    private readonly ConcurrentDictionary<Type, Signal> _signals = new();

    public Signal For(Type contextType) => _signals.GetOrAdd(contextType, static _ => new Signal());

    public void Dispose()
    {
        foreach (var signal in _signals.Values)
        {
            signal.Dispose();
        }
    }

    /// <summary>
    /// A coalescing, single-consumer signal: any number of <see cref="Set"/> calls between two waits wake the relay
    /// exactly once (the pass that follows reads every row committed before it), and a set that arrives while a
    /// pass is running makes the next wait return immediately, so no wake-up is ever lost.
    /// </summary>
    public sealed class Signal : IDisposable
    {
        private readonly SemaphoreSlim _semaphore = new(0, 1);
        private int _pending;

        public void Dispose() => _semaphore.Dispose();

        public void Set()
        {
            if (Interlocked.Exchange(ref _pending, 1) == 0)
            {
                _semaphore.Release();
            }
        }

        /// <summary>Waits for a signal or <paramref name="timeout"/>, whichever comes first; true when signalled.</summary>
        public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            var signalled = await _semaphore.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            if (signalled)
            {
                Interlocked.Exchange(ref _pending, 0);
            }

            return signalled;
        }
    }
}
