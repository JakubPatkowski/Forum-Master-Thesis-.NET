namespace Forum.Infrastructure.Messaging.RabbitMq;

/// <summary>Connection settings for RabbitMQ, bound from the "RabbitMq" configuration section.</summary>
public sealed class RabbitMqOptions
{
    public string Host { get; init; } = "localhost";

    public int Port { get; init; } = 5672;

    public string Username { get; init; } = "guest";

    public string Password { get; init; } = "guest";
}
