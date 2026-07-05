using System.Net.WebSockets;
using System.Text.Json;

using Forum.Infrastructure.Messaging.RabbitMq;

using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Forum.Api.DevTools;

/// <summary>
/// Development-only observability page: <c>GET /dev/monitor</c> serves a self-contained HTML tool that watches
/// BOTH realtime surfaces at once — the raw integration-event stream on RabbitMQ (via the <c>/dev/monitor/bus</c>
/// tap below) and the Phase 7 WebSocket hub as a genuine client (login → ticket → subscribe). The tap uses the
/// hub's own pattern: a server-named exclusive auto-delete queue bound with <c>#</c> to every module exchange —
/// purely passive, it never competes with the module consumers for deliveries. Mapped only in Development.
/// </summary>
internal static class DevMonitorEndpoints
{
    /// <summary>The module topic exchanges (ADR 0009) the tap observes; mirrors the module list in Program.cs.</summary>
    private static readonly string[] Exchanges = ["identity", "content", "files", "engagement"];

    private static readonly JsonSerializerOptions FrameJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static WebApplication MapDevMonitor(this WebApplication app)
    {
        app.MapGet("/dev/monitor", static async context =>
        {
            // The page is inline CSS/JS by design (single file); loosen the global CSP for this response only.
            context.Response.Headers.ContentSecurityPolicy =
                "default-src 'self'; style-src 'unsafe-inline'; script-src 'unsafe-inline'; connect-src 'self' ws: wss:";
            context.Response.ContentType = "text/html; charset=utf-8";

            var assembly = typeof(DevMonitorEndpoints).Assembly;
            var resource = assembly.GetManifestResourceNames()
                .Single(static name => name.EndsWith("monitor.html", StringComparison.Ordinal));
            await using var stream = assembly.GetManifestResourceStream(resource)!;
            await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
        }).ExcludeFromDescription();

        app.MapGet("/dev/monitor/bus", static async (HttpContext context, IRabbitMqConnection broker) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                return Results.BadRequest();
            }

            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            await TapBusAsync(socket, broker, context.RequestAborted);
            return Results.Empty;
        }).ExcludeFromDescription();

        return app;
    }

    private static async Task TapBusAsync(WebSocket socket, IRabbitMqConnection broker, CancellationToken cancellationToken)
    {
        try
        {
            var connection = await broker.GetConnectionAsync(cancellationToken);
            await using var channel = await connection.CreateChannelAsync(options: null, cancellationToken: cancellationToken);

            var queue = (await channel.QueueDeclareAsync(
                queue: string.Empty, durable: false, exclusive: true, autoDelete: true,
                cancellationToken: cancellationToken)).QueueName;
            foreach (var exchange in Exchanges)
            {
                // Same parameters as the relay/consumers — whichever side boots first creates the exchange.
                await channel.ExchangeDeclareAsync(
                    exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);
                await channel.QueueBindAsync(queue, exchange, "#", cancellationToken: cancellationToken);
            }

            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (_, delivery) => ForwardAsync(socket, delivery, cancellationToken);
            await channel.BasicConsumeAsync(queue, autoAck: true, consumer, cancellationToken);

            // Hold the socket open until the client leaves; the tap never expects client messages.
            var buffer = new byte[1024];
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var received = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
                if (received.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
                    break;
                }
            }
        }
        catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
        {
            // Client vanished or host is shutting down — nothing to clean beyond the auto-delete queue.
        }
        catch (Exception exception)
        {
            await TrySendAsync(socket, new BusFrame(
                "error", null, null, null, null, null, false, DateTimeOffset.UtcNow, exception.Message), cancellationToken);
        }
    }

    private static async Task ForwardAsync(WebSocket socket, BasicDeliverEventArgs delivery, CancellationToken cancellationToken)
    {
        object? body;
        try
        {
            body = JsonSerializer.Deserialize<JsonElement>(delivery.Body.Span);
        }
        catch (JsonException)
        {
            body = System.Text.Encoding.UTF8.GetString(delivery.Body.Span);
        }

        var frame = new BusFrame(
            "bus",
            delivery.Exchange,
            delivery.RoutingKey,
            delivery.BasicProperties.MessageId,
            delivery.BasicProperties.Type,
            delivery.BasicProperties.CorrelationId,
            delivery.Redelivered,
            DateTimeOffset.UtcNow,
            body);
        await TrySendAsync(socket, frame, cancellationToken);
    }

    private static async Task TrySendAsync(WebSocket socket, BusFrame frame, CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open)
        {
            return;
        }

        try
        {
            await socket.SendAsync(
                JsonSerializer.SerializeToUtf8Bytes(frame, FrameJson), WebSocketMessageType.Text,
                endOfMessage: true, cancellationToken);
        }
        catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException or OperationCanceledException)
        {
            // A dropped monitor socket is never worth surfacing.
        }
    }

    private sealed record BusFrame(
        string Kind,
        string? Exchange,
        string? RoutingKey,
        string? MessageId,
        string? Type,
        string? CorrelationId,
        bool Redelivered,
        DateTimeOffset ReceivedOnUtc,
        object? Body);
}
