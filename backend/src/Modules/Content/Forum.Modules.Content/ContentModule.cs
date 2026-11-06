using FluentValidation;

using Forum.Common.Cqrs;
using Forum.Common.Messaging;
using Forum.Common.Modules;
using Forum.Infrastructure.Messaging;
using Forum.Infrastructure.Persistence;
using Forum.Infrastructure.Seeding;
using Forum.Modules.Content.Application;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Application.Caching;
using Forum.Modules.Content.Application.Consumers;
using Forum.Modules.Content.Contracts;
using Forum.Modules.Content.Infrastructure.Caching;
using Forum.Modules.Content.Infrastructure.Messaging;
using Forum.Modules.Content.Infrastructure.Persistence;
using Forum.Modules.Content.Infrastructure.Seeding;
using Forum.Modules.Identity.Contracts.IntegrationEvents;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Forum.Modules.Content;

/// <summary>Content module composition: registers its services and maps its endpoints. Everything else is internal.</summary>
public sealed class ContentModule : IModule
{
    public string Name => "Content";

    public IServiceCollection RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ContentDbContext>(configuration.GetConnectionString("Forum") ?? string.Empty);

        // Messaging backbone: relay this module's outbox to its 'content' exchange; consume what it handles.
        services.AddModuleMessaging<ContentDbContext>("content", messaging => messaging
            .Consume<UserBlockedIntegrationEvent>());

        // Persistence ports.
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IThreadRepository, ThreadRepository>();
        services.AddScoped<ICommentRepository, CommentRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<IContentQueries, ContentQueries>();
        services.AddScoped<ContentReadAccess>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();

        // Read cache (A/B v5, docs/specs/READ-CACHE-CONTRACT.md): HybridCache as a pure in-process L1 — no
        // IDistributedCache is registered anywhere, so there is no L2 and no serialization of the stored entries.
        // Off by default (ReadCache:Enabled); the per-round switch of the benchmark sets it on both architectures.
        services.Configure<ReadCacheOptions>(configuration.GetSection(ReadCacheOptions.SectionName));
        services.AddMemoryCache(static options =>
            // The L1 is a MemoryCache: expired entries leave it on the next expiration scan (default every minute).
            // With 2–3 s TTLs that would keep up to a minute of dead pages in memory under churn — scan often instead.
            options.ExpirationScanFrequency = TimeSpan.FromSeconds(5));
        services.AddHybridCache(static options =>
            options.DefaultEntryOptions = new Microsoft.Extensions.Caching.Hybrid.HybridCacheEntryOptions
            {
                Flags = Microsoft.Extensions.Caching.Hybrid.HybridCacheEntryFlags.DisableDistributedCache,
            });
        services.AddSingleton(static provider =>
            new ReadCacheState(provider.GetRequiredService<IOptions<ReadCacheOptions>>().Value.MaxEntriesPerCache));
        services.AddScoped<IContentReadCache, HybridContentReadCache>();

        // Cross-module consumers (dispatched in-process now, via the RabbitMQ relay from Phase 6).
        services.AddScoped<IIntegrationEventHandler<UserBlockedIntegrationEvent>, UserBlockedEventHandler>();

        // Contracts surface: lets Files gate attach/detach with Content's own ownership/moderation rules,
        // and lets the WebSocket hub re-check category visibility before every push (Phase 7).
        services.AddScoped<IContentAuthorization, ContentAttachmentAuthorizer>();
        services.AddScoped<IContentVisibility, CategoryAccessReader>();

        // Offline deterministic seeder (Phase 9b) — resolved only by the `seed` CLI entrypoint, never on boot.
        services.AddScoped<IModuleSeeder, ContentSeeder>();

        // Validators + CQRS handlers (handlers are internal, hence the non-public scans).
        services.AddValidatorsFromAssembly(AssemblyReference.Assembly, includeInternalTypes: true);
        services.Scan(scan => scan
            .FromAssemblies(AssemblyReference.Assembly)
            .AddClasses(filter => filter.AssignableTo(typeof(ICommandHandler<>)), publicOnly: false)
                .AsImplementedInterfaces().WithScopedLifetime()
            .AddClasses(filter => filter.AssignableTo(typeof(ICommandHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces().WithScopedLifetime()
            .AddClasses(filter => filter.AssignableTo(typeof(IQueryHandler<,>)), publicOnly: false)
                .AsImplementedInterfaces().WithScopedLifetime());

        return services;
    }

    public IEndpointRouteBuilder MapEndpoints(IEndpointRouteBuilder endpoints) =>
        endpoints.MapEndpointsFrom(AssemblyReference.Assembly);
}
