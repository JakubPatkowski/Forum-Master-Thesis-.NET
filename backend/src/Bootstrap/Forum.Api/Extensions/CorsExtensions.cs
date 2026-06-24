namespace Forum.Api.Extensions;

/// <summary>CORS for the decoupled React SPA: an explicit origin allow-list from configuration (never a wildcard with credentials).</summary>
public static class CorsExtensions
{
    public const string SpaPolicy = "forum-spa";

    public static IServiceCollection AddForumCors(this IServiceCollection services, IConfiguration configuration)
    {
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        services.AddCors(options => options.AddPolicy(SpaPolicy, policy => policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));

        return services;
    }
}
