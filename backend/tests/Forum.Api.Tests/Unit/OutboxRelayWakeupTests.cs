using System.Diagnostics;

using Forum.Infrastructure.Messaging.Outbox;

using Shouldly;

using Xunit;

namespace Forum.Api.Tests.Unit;

/// <summary>The relay wake-up signal (A/B v5, A4): coalescing, never lost, and a timed-out wait is the poll fallback.</summary>
public sealed class OutboxRelayWakeupTests
{
    [Fact]
    public async Task A_set_before_the_wait_wakes_the_relay_immediately()
    {
        using var wakeup = new OutboxRelayWakeup();
        var signal = wakeup.For(typeof(string));

        signal.Set();
        var watch = Stopwatch.StartNew();
        var signalled = await signal.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);

        signalled.ShouldBeTrue();
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Many_sets_between_two_waits_wake_exactly_once()
    {
        using var wakeup = new OutboxRelayWakeup();
        var signal = wakeup.For(typeof(string));

        signal.Set();
        signal.Set();
        signal.Set();

        (await signal.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)).ShouldBeTrue();
        (await signal.WaitAsync(TimeSpan.FromMilliseconds(100), CancellationToken.None))
            .ShouldBeFalse("the coalesced signal was consumed by the first wait");
    }

    [Fact]
    public async Task A_set_during_a_pass_wakes_the_next_wait_without_a_poll()
    {
        using var wakeup = new OutboxRelayWakeup();
        var signal = wakeup.For(typeof(string));

        signal.Set();
        (await signal.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)).ShouldBeTrue();
        signal.Set(); // a write committed while the relay was publishing the previous batch
        (await signal.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None)).ShouldBeTrue();
    }

    [Fact]
    public async Task Without_a_set_the_wait_times_out_at_the_poll_interval()
    {
        using var wakeup = new OutboxRelayWakeup();
        var signal = wakeup.For(typeof(string));

        var watch = Stopwatch.StartNew();
        var signalled = await signal.WaitAsync(TimeSpan.FromMilliseconds(150), CancellationToken.None);

        signalled.ShouldBeFalse();
        watch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(140));
    }

    [Fact]
    public void Each_module_context_gets_its_own_signal()
    {
        using var wakeup = new OutboxRelayWakeup();

        wakeup.For(typeof(string)).ShouldBeSameAs(wakeup.For(typeof(string)));
        wakeup.For(typeof(string)).ShouldNotBeSameAs(wakeup.For(typeof(int)));
    }
}
