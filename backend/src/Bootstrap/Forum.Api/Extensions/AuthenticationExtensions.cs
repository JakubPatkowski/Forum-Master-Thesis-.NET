using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Forum.Api.Extensions;

/// <summary>
/// Phase-0 auth skeleton so the pipeline's UseAuthentication/UseAuthorization are real. JWT bearer validates issuer/
/// audience/lifetime; the signing key and token issuance (plus the httpOnly refresh cookie) are wired in Phase 1.
/// </summary>
public static class AuthenticationExtensions
{
    public static IServiceCollection AddForumAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = false, // signing key configured in Phase 1
                    ValidIssuer = configuration["Jwt:Issuer"],
                    ValidAudience = configuration["Jwt:Audience"],
                };
            });

        services.AddAuthorization();
        return services;
    }
}
