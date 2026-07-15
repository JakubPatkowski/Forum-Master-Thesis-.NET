using System.Net.WebSockets;

namespace Forum.Api.Realtime;

/// <summary>
/// One authenticated socket: the user it belongs to, its subscribed views, and a serialized send path
/// (<see cref="WebSocket.SendAsync(ArraySegment{byte}, WebSocketMessageType, bool, CancellationToken)"/> allows
/// only one outstanding send per socket, and the dispatcher runs concurrently with control-frame replies).
/// </summary>
internal sealed class RealtimeConnection : IDisposable
{
    // Sends normally complete in microseconds (kernel buffer); only a peer that stopped reading with a
    // full TCP window can make one block. The bus delivers events to the dispatcher one at a time, so a
    // single such socket would otherwise stall pushes to EVERY subscriber on this replica, indefinitely.
    private static readonly TimeSpan DefaultSendTimeout = TimeSpan.FromSeconds(5);

    private readonly WebSocket _socket;
    private readonly TimeSpan _sendTimeout;
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    public RealtimeConnection(WebSocket socket, Ulid userId, TimeSpan? sendTimeout = null)
    {
        _socket = socket;
        _sendTimeout = sendTimeout ?? DefaultSendTimeout;
        UserId = userId;
    }

    public Guid Id { get; } = Guid.NewGuid();

    public Ulid UserId { get; }

    public SubscriptionSet Subscriptions { get; } = new();

    /// <summary>
    /// Sends one text frame; false when the socket is no longer usable. Send failures are terminal for the
    /// socket, never for the hub — the client reconnects and resyncs (ADR 0010), so nothing is buffered or retried.
    /// A send (or the wait for one already in flight) that outlives the timeout aborts the socket for the same
    /// reason: a stalled peer must cost the hub one connection, not head-of-line-block every other subscriber.
    /// </summary>
    public async Task<bool> TrySendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (_socket.State != WebSocketState.Open)
        {
            return false;
        }

        bool acquired;
        try
        {
            acquired = await _sendGate.WaitAsync(_sendTimeout, cancellationToken);
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException)
        {
            return false;
        }

        if (!acquired)
        {
            Abort();
            return false;
        }

        try
        {
            using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            sendTimeout.CancelAfter(_sendTimeout);
            await _socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, sendTimeout.Token);
            return true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Abort();
            return false;
        }
        catch (Exception exception) when (exception is OperationCanceledException or WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }
        finally
        {
            _sendGate.Release();
        }
    }

    /// <summary>Hard-drop a stalled socket so its read loop unwinds and unregisters it; never throws.</summary>
    private void Abort()
    {
        try
        {
            _socket.Abort();
        }
        catch (ObjectDisposedException)
        {
            // Raced the handler's dispose — the socket is already gone, which is the outcome we wanted.
        }
    }

    public void Dispose() => _sendGate.Dispose();
}
