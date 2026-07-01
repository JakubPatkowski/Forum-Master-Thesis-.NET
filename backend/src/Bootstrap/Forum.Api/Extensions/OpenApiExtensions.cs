using Microsoft.OpenApi;

namespace Forum.Api.Extensions;

/// <summary>
/// OpenAPI document generation with a JWT Bearer security scheme, so the Swagger UI "Authorize" button works for the
/// protected endpoints. The document is served at <c>/openapi/v1.json</c> and rendered by Swagger UI at <c>/swagger</c>.
/// </summary>
public static class OpenApiExtensions
{
    private const string BearerScheme = "Bearer";

    public static IServiceCollection AddForumOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
            options.AddDocumentTransformer((document, context, cancellationToken) =>
            {
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[BearerScheme] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    In = ParameterLocation.Header,
                    Description = "Paste the access token returned by POST /api/identity/login.",
                };

                document.Security ??= new List<OpenApiSecurityRequirement>();
                document.Security.Add(new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference(BearerScheme, document, null)] = new List<string>(),
                });

                return Task.CompletedTask;
            }));

        return services;
    }
}
