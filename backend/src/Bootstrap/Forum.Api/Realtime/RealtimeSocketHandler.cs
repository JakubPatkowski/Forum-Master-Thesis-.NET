using System.Net.WebSockets;
using System.Text.Json;

using Microsoft.Extensions.Options;

namespace Forum.Api.Realtime;

/// <summary>What a client sends over the socket: subscribe/unsubscribe to a category/thread/user view.</summary>
internal sealed record RealtimeClientMessage(string? Action, string? View, string? Id);

/// <summary>Control frames the hub sends back: subscribe/unsubscribe acks and protocol errors.</summary>
internal sealed record RealtimeControlMessage(string Type, string? View = null, string? Id = null, string? Reason = null);

/// <summary>
/// Owns an accepted socket's lifecycle: registers it for pushes, runs the read loop for the small
/// subscribe/unsubscribe protocol, and unregisters on any exit. Subscribing is acked (<c>subscribed</c>) so the
/// client knows when pushes for a view can start; there is no replay or catch-up — after (re)connecting the
/// client re-fetches its current view (ADR 0010's resync) and only then relies on patches.
/// </summary>
internal sealed class RealtimeSocketHandler
{
    private const int MaxMessageBytes = 4096;

    private readonly RealtimeConnectionRegistry _registry;
    private readonly RealtimeOptions _options;
    private readonly ILogger<RealtimeSocketHandler> _logger;

    public RealtimeSocketHandler(
        RealtimeConnectionRegistry registry,
        IOptions<RealtimeOptions> options,
        ILogger<RealtimeSocketHandler> logger)
    {
        _registry = registry;
        _options = options.Value;
        _logger = logger;
    }

    public async Task HandleAsync(WebSocket socket, Ulid userId, CancellationToken cancellationToken)
    {
        using var connection = new RealtimeConnection(socket, userId);
        _registry.Add(connection);
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug("Realtime socket {ConnectionId} opened for user {UserId}.", connection.Id, userId);
        }

        try
        {
            var buffer = new byte[MaxMessageBytes];
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var received = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
                if (received.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
                    break;
                }

                if (received.MessageType != WebSocketMessageType.Text || !received.EndOfMessage)
                {
                    // Oversized or binary frames are a protocol violation worth ending the session over.
                    await socket.CloseAsync(
                        WebSocketCloseStatus.PolicyViolation, "text frames up to 4 KiB only", cancellationToken);
                    break;
                }

                var reply = Process(connection, buffer.AsSpan(0, received.Count));
                await connection.TrySendAsync(
                    JsonSerializer.SerializeToUtf8Bytes(reply, RealtimeJson.Options), cancellationToken);
            }
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
        {
            // Abrupt client disconnect or host shutdown — either way the finally unregisters the socket.
        }
        finally
        {
            _registry.Remove(connection);
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Realtime socket {ConnectionId} closed.", connection.Id);
            }
        }
    }

    private RealtimeControlMessage Process(RealtimeConnection connection, ReadOnlySpan<byte> message)
    {
        RealtimeClientMessage? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<RealtimeClientMessage>(message, RealtimeJson.Options);
        }
        catch (JsonException)
        {
            parsed = null;
        }

        if (parsed is null)
        {
            return new RealtimeControlMessage("error", Reason: "malformed-message");
        }

        if (!SubscriptionView.TryParse(parsed.View, parsed.Id, out var view))
        {
            return new RealtimeControlMessage("error", parsed.View, parsed.Id, "unknown-view");
        }

        switch (parsed.Action)
        {
            case "subscribe":
                // A user view is self-only: watching someone else's would leak their activity stream.
                if (view.Kind == ViewKind.User && view.Id != connection.UserId)
                {
                    return new RealtimeControlMessage("error", parsed.View, parsed.Id, "forbidden-view");
                }

                if (!connection.Subscriptions.TryAdd(view, _options.MaxSubscriptionsPerConnection))
                {
                    return new RealtimeControlMessage("error", parsed.View, parsed.Id, "too-many-subscriptions");
                }

                return new RealtimeControlMessage("subscribed", parsed.View, parsed.Id);

            case "unsubscribe":
                connection.Subscriptions.Remove(view);
                return new RealtimeControlMessage("unsubscribed", parsed.View, parsed.Id);

            default:
                return new RealtimeControlMessage("error", parsed.View, parsed.Id, "unknown-action");
        }
    }
}
