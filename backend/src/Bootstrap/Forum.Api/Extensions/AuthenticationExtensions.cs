using Forum.Common.Security;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Forum.Api.Extensions;

/// <summary>
/// JWT bearer authentication: validates issuer/audience/lifetime and the HS256 signature against the configured
/// signing key (a k8s Secret in the cluster; a clearly-marked development fallback locally — see <see cref="JwtOptions"/>).
/// </summary>
public static class AuthenticationExtensions
{
    public static IServiceCollection AddForumAuthentication(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

        // G19: the development fallback key is publicly visible in source — a Production boot without a real
        // key must fail loudly at startup, never silently sign tokens anyone can forge.
        if (environment.IsProduction() && string.IsNullOrWhiteSpace(jwt.SigningKey))
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must be provided in Production (via the k8s Secret); "
                + "the built-in development signing key must never sign production tokens.");
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(jwt.SigningKeyBytes()),
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorization();
        return services;
    }
}
