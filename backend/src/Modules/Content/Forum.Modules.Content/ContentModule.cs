using FluentValidation;

using Forum.Common.Cqrs;
using Forum.Common.Messaging;
using Forum.Common.Modules;
using Forum.Infrastructure.Persistence;
using Forum.Modules.Content.Application;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Application.Consumers;
using Forum.Modules.Content.Contracts;
using Forum.Modules.Content.Infrastructure.Messaging;
using Forum.Modules.Content.Infrastructure.Persistence;
using Forum.Modules.Identity.Contracts.IntegrationEvents;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Forum.Modules.Content;

/// <summary>Content module composition: registers its services and maps its endpoints. Everything else is internal.</summary>
public sealed class ContentModule : IModule
{
    public string Name => "Content";

    public IServiceCollection RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<ContentDbContext>(configuration.GetConnectionString("Forum") ?? string.Empty);

        // Persistence ports.
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IThreadRepository, ThreadRepository>();
        services.AddScoped<ICommentRepository, CommentRepository>();
        services.AddScoped<ITagRepository, TagRepository>();
        services.AddScoped<IContentQueries, ContentQueries>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();

        // Cross-module consumers (dispatched in-process now, via the RabbitMQ relay from Phase 6).
        services.AddScoped<IIntegrationEventHandler<UserBlockedIntegrationEvent>, UserBlockedEventHandler>();

        // Contracts surface: lets Files gate attach/detach with Content's own ownership/moderation rules.
        services.AddScoped<IContentAuthorization, ContentAttachmentAuthorizer>();

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
