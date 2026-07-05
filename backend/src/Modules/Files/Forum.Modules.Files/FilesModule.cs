using FluentValidation;

using Forum.Common.Cqrs;
using Forum.Common.Messaging;
using Forum.Common.Modules;
using Forum.Infrastructure.Messaging;
using Forum.Infrastructure.Persistence;
using Forum.Modules.Content.Contracts.IntegrationEvents;
using Forum.Modules.Files.Application;
using Forum.Modules.Files.Application.Abstractions;
using Forum.Modules.Files.Application.Consumers;
using Forum.Modules.Files.Application.Sweep;
using Forum.Modules.Files.Infrastructure.Messaging;
using Forum.Modules.Files.Infrastructure.Persistence;
using Forum.Modules.Files.Infrastructure.Sweep;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Forum.Modules.Files;

/// <summary>Files module composition: registers its services and maps its endpoints. Everything else is internal.</summary>
public sealed class FilesModule : IModule
{
    public string Name => "Files";

    public IServiceCollection RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<FilesDbContext>(configuration.GetConnectionString("Forum") ?? string.Empty);

        services.Configure<FilesOptions>(configuration.GetSection(FilesOptions.SectionName));

        // Messaging backbone: relay this module's outbox to its 'files' exchange; consume what it handles.
        services.AddModuleMessaging<FilesDbContext>("files", messaging => messaging
            .Consume<ThreadDeletedIntegrationEvent>()
            .Consume<CommentDeletedIntegrationEvent>());

        // Persistence ports.
        services.AddScoped<IStoredFileRepository, StoredFileRepository>();
        services.AddScoped<IFilesQueries, FilesQueries>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();

        // Orphan sweep: recurring background job (NOT a one-shot IStartupTask) + cross-replica advisory lock.
        services.AddScoped<ISweepLock, AdvisorySweepLock>();
        services.AddScoped<OrphanSweeper>();
        services.AddHostedService<OrphanSweepService>();

        // Cross-module consumers (dispatched in-process now, via the RabbitMQ relay from Phase 6).
        services.AddScoped<IIntegrationEventHandler<ThreadDeletedIntegrationEvent>, ThreadDeletedEventHandler>();
        services.AddScoped<IIntegrationEventHandler<CommentDeletedIntegrationEvent>, CommentDeletedEventHandler>();

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
