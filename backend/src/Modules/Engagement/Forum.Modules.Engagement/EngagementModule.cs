using FluentValidation;

using Forum.Common.Cqrs;
using Forum.Common.Messaging;
using Forum.Common.Modules;
using Forum.Infrastructure.Messaging;
using Forum.Infrastructure.Persistence;
using Forum.Modules.Content.Contracts.IntegrationEvents;
using Forum.Modules.Engagement.Application.Abstractions;
using Forum.Modules.Engagement.Application.Consumers;
using Forum.Modules.Engagement.Infrastructure.Messaging;
using Forum.Modules.Engagement.Infrastructure.Persistence;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Forum.Modules.Engagement;

/// <summary>Engagement module composition: registers its services and maps its endpoints. Everything else is internal.</summary>
public sealed class EngagementModule : IModule
{
    public string Name => "Engagement";

    public IServiceCollection RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<EngagementDbContext>(configuration.GetConnectionString("Forum") ?? string.Empty);

        // Messaging backbone: relay this module's outbox to its 'engagement' exchange; consume what it handles.
        services.AddModuleMessaging<EngagementDbContext>("engagement", messaging => messaging
            .Consume<ThreadDeletedIntegrationEvent>()
            .Consume<CommentDeletedIntegrationEvent>());

        // Persistence ports.
        services.AddScoped<IReactionRepository, ReactionRepository>();
        services.AddScoped<IReactionTargetReader, ContentTargetReader>();
        services.AddScoped<IEngagementQueries, EngagementQueries>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();

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
