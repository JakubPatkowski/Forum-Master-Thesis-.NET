using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Forum.Common.Security;

/// <summary>
/// Endpoint filter that gates a route on a permission resolved by the SQL ACL. Returns 401 when anonymous and
/// 403 when the bit is not set — the resource-existence (404) check stays in the handler, preserving 404 → 403 → 422.
/// </summary>
public static class RequirePermissionExtensions
{
    /// <summary>Requires the current user to hold <paramref name="action"/> at <paramref name="scope"/> (global by default).</summary>
    public static TBuilder RequirePermission<TBuilder>(
        this TBuilder builder, string action, string scope = PermissionScopes.Global)
        where TBuilder : IEndpointConventionBuilder =>
        builder
            .RequireAuthorization()
            .AddEndpointFilter(new RequirePermissionFilter(action, scope));
}

internal sealed class RequirePermissionFilter : IEndpointFilter
{
    private readonly string _action;
    private readonly string _scope;

    public RequirePermissionFilter(string action, string scope)
    {
        _action = action;
        _scope = scope;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var user = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

        if (!user.IsAuthenticated)
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Authentication required.");
        }

        var allowed = await user.HasPermissionAsync(_action, _scope, scopeId: null, context.HttpContext.RequestAborted);
        if (!allowed)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: $"Missing permission '{_action}'.");
        }

        return await next(context);
    }
}
