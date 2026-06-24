using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Forum.Infrastructure.Persistence;

/// <summary>Registers a module's DbContext with the shared conventions (Npgsql, snake_case, audit interceptor) and exposes it to the migration runner.</summary>
public static class ModuleDbContextRegistration
{
    public static IServiceCollection AddModuleDbContext<TContext>(
        this IServiceCollection services, string connectionString)
        where TContext : ForumDbContext
    {
        services.AddDbContext<TContext>((provider, options) => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention()
            .AddInterceptors(provider.GetRequiredService<AuditInterceptor>()));

        // Expose every module context to the migration runner (ADR 0005).
        services.AddScoped<DbContext>(provider => provider.GetRequiredService<TContext>());
        return services;
    }
}
