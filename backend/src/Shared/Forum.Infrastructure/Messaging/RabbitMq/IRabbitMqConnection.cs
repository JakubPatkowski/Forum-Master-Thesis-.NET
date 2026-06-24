using RabbitMQ.Client;

namespace Forum.Infrastructure.Messaging.RabbitMq;

/// <summary>Lazily provides a shared RabbitMQ connection. Registered now; consumers and the outbox relay are wired in Phase 6.</summary>
public interface IRabbitMqConnection : IAsyncDisposable
{
    ValueTask<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default);
}
