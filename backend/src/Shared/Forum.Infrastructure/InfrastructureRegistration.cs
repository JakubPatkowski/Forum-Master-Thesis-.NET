using Forum.Common.Messaging;
using Forum.Common.Security;
using Forum.Infrastructure.Messaging;
using Forum.Infrastructure.Messaging.RabbitMq;
using Forum.Infrastructure.Persistence;
using Forum.Infrastructure.Storage;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Minio;

namespace Forum.Infrastructure;

/// <summary>Registers the shared infrastructure: clock, audit, in-process bus, lazy RabbitMQ connection and MinIO client.</summary>
public static class InfrastructureRegistration
{
    public static IServiceCollection AddForumInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(TimeProvider.System);

        // Default "no authenticated user"; the Identity module replaces this with an HTTP-aware actor in Phase 1.
        services.TryAddScoped<ICurrentActor, UnauthenticatedActor>();

        // Persistence cross-cutting (consumed by each module's DbContext registration).
        services.AddScoped<AuditInterceptor>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        // In-process integration bus (RabbitMQ outbox relay replaces this in Phase 6).
        services.AddSingleton<IEventBus, InMemoryEventBus>();

        // RabbitMQ connection — registered, opened lazily, not consumed yet.
        services.Configure<RabbitMqOptions>(configuration.GetSection("RabbitMq"));
        services.AddSingleton<IRabbitMqConnection, RabbitMqConnection>();

        // MinIO object storage.
        services.Configure<StorageOptions>(configuration.GetSection("Storage"));
        services.AddSingleton<IMinioClient>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<StorageOptions>>().Value;
            return new MinioClient()
                .WithEndpoint(options.Endpoint)
                .WithCredentials(options.AccessKey, options.SecretKey)
                .WithSSL(options.UseSsl)
                .Build();
        });
        services.AddSingleton<IObjectStorage, MinioObjectStorage>();

        return services;
    }
}
