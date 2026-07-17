using FluentValidation;

using Forum.Common.Cqrs;
using Forum.Common.Messaging;
using Forum.Common.Modules;
using Forum.Infrastructure.Messaging;
using Forum.Infrastructure.Persistence;
using Forum.Infrastructure.Seeding;
using Forum.Modules.Identity.Contracts.IntegrationEvents;
using Forum.Modules.Social.Application;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Application.Consumers;
using Forum.Modules.Social.Contracts;
using Forum.Modules.Social.Infrastructure.Messaging;
using Forum.Modules.Social.Infrastructure.Persistence;
using Forum.Modules.Social.Infrastructure.Seeding;

using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Forum.Modules.Social;

/// <summary>Social module composition: registers its services and maps its endpoints. Everything else is internal.</summary>
public sealed class SocialModule : IModule
{
    public string Name => "Social";

    public IServiceCollection RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDbContext<SocialDbContext>(configuration.GetConnectionString("Forum") ?? string.Empty);
        services.Configure<SocialOptions>(configuration.GetSection(SocialOptions.SectionName));

        // Messaging backbone: relay this module's outbox to its 'social' exchange; consume Identity's admin ban.
        services.AddModuleMessaging<SocialDbContext>("social", messaging => messaging
            .Consume<UserBlockedIntegrationEvent>());

        // Persistence ports.
        services.AddScoped<IFriendshipRepository, FriendshipRepository>();
        services.AddScoped<ISocialBlockRepository, SocialBlockRepository>();
        services.AddScoped<IGroupRepository, GroupRepository>();
        services.AddScoped<IGroupInviteRepository, GroupInviteRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<IPrivacySettingsRepository, PrivacySettingsRepository>();
        services.AddScoped<IUserReader, UserReader>();
        services.AddScoped<ISocialQueries, SocialQueries>();
        services.AddScoped<IPresenceStore, PostgresPresenceStore>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();

        // Application services shared across handlers.
        services.AddScoped<SocialInteractionGate>();
        services.AddScoped<Notifier>();

        // Contracts surfaces consumed outside the module (realtime hub + Files).
        services.AddScoped<ISocialVisibility, SocialVisibilityReader>();
        services.AddScoped<ISocialAuthorization, SocialAttachmentAuthorizer>();

        // Cross-module consumers (via the RabbitMQ backbone).
        services.AddScoped<IIntegrationEventHandler<UserBlockedIntegrationEvent>, UserBlockedEventHandler>();

        // Offline deterministic seeder (Phase 9b pattern) — resolved only by the `seed` CLI entrypoint.
        services.AddScoped<IModuleSeeder, SocialSeeder>();

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
