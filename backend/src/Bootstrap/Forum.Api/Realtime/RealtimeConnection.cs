using System.Net.WebSockets;

namespace Forum.Api.Realtime;

/// <summary>
/// One authenticated socket: the user it belongs to, its subscribed views, and a serialized send path
/// (<see cref="WebSocket.SendAsync(ArraySegment{byte}, WebSocketMessageType, bool, CancellationToken)"/> allows
/// only one outstanding send per socket, and the dispatcher runs concurrently with control-frame replies).
/// </summary>
internal sealed class RealtimeConnection : IDisposable
{
    private readonly WebSocket _socket;
    private readonly SemaphoreSlim _sendGate = new(1, 1);

    public RealtimeConnection(WebSocket socket, Ulid userId)
    {
        _socket = socket;
        UserId = userId;
    }

    public Guid Id { get; } = Guid.NewGuid();

    public Ulid UserId { get; }

    public SubscriptionSet Subscriptions { get; } = new();

    /// <summary>
    /// Sends one text frame; false when the socket is no longer usable. Send failures are terminal for the
    /// socket, never for the hub — the client reconnects and resyncs (ADR 0010), so nothing is buffered or retried.
    /// </summary>
    public async Task<bool> TrySendAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (_socket.State != WebSocketState.Open)
        {
            return false;
        }

        await _sendGate.WaitAsync(cancellationToken);
        try
        {
            await _socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException or InvalidOperationException)
        {
            return false;
        }
        finally
        {
            _sendGate.Release();
        }
    }

    public void Dispose() => _sendGate.Dispose();
}
