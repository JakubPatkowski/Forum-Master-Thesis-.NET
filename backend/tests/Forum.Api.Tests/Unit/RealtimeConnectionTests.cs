using System.Net.WebSockets;

using Forum.Api.Realtime;

using Shouldly;

using Xunit;

namespace Forum.Api.Tests.Unit;

/// <summary>
/// The send path's stalled-peer guard: the bus hands the dispatcher one event at a time, so a socket
/// whose peer stopped reading (full TCP window — SendAsync never completes) must be aborted after the
/// send timeout instead of head-of-line-blocking pushes to every other subscriber on the replica.
/// </summary>
public sealed class RealtimeConnectionTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromMilliseconds(200);

    [Fact]
    public async Task Send_that_completes_returns_true()
    {
        using var connection = new RealtimeConnection(new FakeWebSocket(), Ulid.NewUlid(), TestTimeout);

        (await connection.TrySendAsync(new byte[] { 1 }, CancellationToken.None)).ShouldBeTrue();
    }

    [Fact]
    public async Task Stalled_send_returns_false_and_aborts_the_socket()
    {
        var socket = new FakeWebSocket { StallSends = true };
        using var connection = new RealtimeConnection(socket, Ulid.NewUlid(), TestTimeout);

        (await connection.TrySendAsync(new byte[] { 1 }, CancellationToken.None)).ShouldBeFalse();
        socket.Aborted.ShouldBeTrue();
    }

    [Fact]
    public async Task Sends_after_an_abort_fail_fast()
    {
        var socket = new FakeWebSocket { StallSends = true };
        using var connection = new RealtimeConnection(socket, Ulid.NewUlid(), TestTimeout);
        (await connection.TrySendAsync(new byte[] { 1 }, CancellationToken.None)).ShouldBeFalse();

        // The aborted socket is no longer Open — later dispatches skip it without touching the gate.
        (await connection.TrySendAsync(new byte[] { 2 }, CancellationToken.None)).ShouldBeFalse();
    }

    [Fact]
    public async Task Caller_cancellation_returns_false_without_aborting()
    {
        var socket = new FakeWebSocket { StallSends = true };
        using var connection = new RealtimeConnection(socket, Ulid.NewUlid(), TimeSpan.FromSeconds(30));
        using var cts = new CancellationTokenSource(TestTimeout);

        (await connection.TrySendAsync(new byte[] { 1 }, cts.Token)).ShouldBeFalse();
        socket.Aborted.ShouldBeFalse();
    }

    /// <summary>Open WebSocket whose sends either complete instantly or park forever until Abort.</summary>
    private sealed class FakeWebSocket : WebSocket
    {
        private readonly TaskCompletionSource _never = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool StallSends { get; init; }

        public bool Aborted { get; private set; }

        public override WebSocketState State => Aborted ? WebSocketState.Aborted : WebSocketState.Open;

        public override string? SubProtocol => null;

        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override void Abort()
        {
            Aborted = true;
            _never.TrySetException(new WebSocketException(WebSocketError.ConnectionClosedPrematurely));
        }

        public override Task SendAsync(
            ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
            => StallSends ? _never.Task.WaitAsync(cancellationToken) : Task.CompletedTask;

        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus, string? statusDescription, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public override void Dispose()
        {
        }
    }
}
