using Microsoft.Extensions.Options;

using RabbitMQ.Client;

namespace Forum.Infrastructure.Messaging.RabbitMq;

/// <summary>Opens the RabbitMQ connection on first use (never at boot), so a missing broker does not block startup or liveness.</summary>
internal sealed class RabbitMqConnection : IRabbitMqConnection
{
    private readonly ConnectionFactory _factory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public RabbitMqConnection(IOptions<RabbitMqOptions> options)
    {
        var value = options.Value;
        _factory = new ConnectionFactory
        {
            HostName = value.Host,
            Port = value.Port,
            UserName = value.Username,
            Password = value.Password,
        };
    }

    public async ValueTask<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is { IsOpen: true })
        {
            return _connection;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is not { IsOpen: true })
            {
                _connection = await _factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
            }

            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }

        _gate.Dispose();
    }
}
