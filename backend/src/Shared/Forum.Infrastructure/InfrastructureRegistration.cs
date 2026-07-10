using Forum.Common.Messaging;
using Forum.Common.Security;
using Forum.Common.Telemetry;
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

        // The domain Meter (Phase 9a). One singleton for the whole host; the host exports it via AddMeter(MeterName).
        services.AddSingleton<ForumMetrics>();

        // Default "no authenticated user"; the Identity module replaces this with an HTTP-aware actor in Phase 1.
        services.TryAddScoped<ICurrentActor, UnauthenticatedActor>();

        // Persistence cross-cutting (consumed by each module's DbContext registration).
        services.AddScoped<AuditInterceptor>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        // In-process dispatch stage of the messaging backbone: the per-module RabbitMQ consumer hosts feed
        // wire-delivered events into it. Scoped, so handlers resolve from the active consumer scope.
        services.AddScoped<IEventBus, InMemoryEventBus>();

        // RabbitMQ connection (lazy, shared by the outbox relays, consumer hosts and the readiness check).
        services.Configure<RabbitMqOptions>(configuration.GetSection("RabbitMq"));
        services.Configure<MessagingOptions>(configuration.GetSection(MessagingOptions.SectionName));
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
