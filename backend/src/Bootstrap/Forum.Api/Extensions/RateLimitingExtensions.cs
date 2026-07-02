using System.Threading.RateLimiting;

using Forum.Common.Security;

namespace Forum.Api.Extensions;

/// <summary>
/// A baseline per-IP fixed-window limiter plus a tighter named policy for authentication endpoints.
/// Limits are configurable (<c>RateLimiting:{Global,Auth}:PermitLimit</c>) so tests and load benchmarks can
/// raise them without code changes; the defaults are the production posture.
/// </summary>
public static class RateLimitingExtensions
{
    public static IServiceCollection AddForumRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var globalLimit = configuration.GetValue("RateLimiting:Global:PermitLimit", 100);
        var authLimit = configuration.GetValue("RateLimiting:Auth:PermitLimit", 10);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = globalLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));

            // Tighter per-IP limit for login/register (credential stuffing / enumeration defence).
            options.AddPolicy(RateLimitPolicies.Auth, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = authLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });

        return services;
    }
}
