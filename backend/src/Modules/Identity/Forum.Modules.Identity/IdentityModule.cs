using FluentValidation;
using Forum.Common.Cqrs;
using Forum.Common.Modules;
using Forum.Common.Security;
using Forum.Infrastructure.Persistence;
using Forum.Modules.Identity.Application.Abstractions;
using Forum.Modules.Identity.Infrastructure.Authorization;
using Forum.Modules.Identity.Infrastructure.Messaging;
using Forum.Modules.Identity.Infrastructure.Persistence;
using Forum.Modules.Identity.Infrastructure.Security;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Forum.Modules.Identity;

/// <summary>Identity module composition: registers its services and maps its endpoints. Everything else is internal.</summary>
public sealed class IdentityModule : IModule
{
    public string Name => "Identity";

    public IServiceCollection RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddHttpContextAccessor();

        services.AddModuleDbContext<IdentityDbContext>(configuration.GetConnectionString("Forum") ?? string.Empty);

        // Persistence ports.
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IUserQueries, UserQueries>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();

        // Authorization (SQL ACL): resolution is shared; administration/cache is module-internal.
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IAuthorizationStore, AuthorizationStore>();

        // Current principal — backs ICurrentUser and ICurrentActor (registered before AddForumInfrastructure's TryAdd).
        services.AddScoped<CurrentUser>();
        services.AddScoped<ICurrentUser>(static provider => provider.GetRequiredService<CurrentUser>());
        services.AddScoped<ICurrentActor>(static provider => provider.GetRequiredService<CurrentUser>());

        // Stateless crypto services.
        services.AddSingleton<Argon2PasswordHasher>();
        services.AddSingleton<IPasswordHasher>(static provider => provider.GetRequiredService<Argon2PasswordHasher>());
        services.AddSingleton<IPasswordVerifier>(static provider => provider.GetRequiredService<Argon2PasswordHasher>());
        services.AddSingleton<IJwtTokenService, JwtTokenService>();
        services.AddSingleton<IRefreshTokenService, RefreshTokenService>();

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
