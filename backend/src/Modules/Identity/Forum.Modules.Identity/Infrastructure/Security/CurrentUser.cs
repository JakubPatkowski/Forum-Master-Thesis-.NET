using System.Globalization;
using System.IdentityModel.Tokens.Jwt;

using Forum.Common.Security;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Forum.Modules.Identity.Infrastructure.Security;

/// <summary>
/// Resolves the current principal from the validated JWT (the <c>sub</c> and <c>role</c> claims) and answers
/// permission questions via the SQL ACL. Also backs <see cref="ICurrentActor"/> so the audit interceptor stamps the
/// real user. Scoped per request.
/// </summary>
/// <remarks>
/// <see cref="IPermissionService"/> is resolved lazily from the request scope (not constructor-injected) to avoid a
/// DI cycle: the DbContext depends on the audit interceptor, which depends on this (as <see cref="ICurrentActor"/>),
/// while the permission service depends on the DbContext.
/// </remarks>
internal sealed class CurrentUser : ICurrentUser, ICurrentActor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

    public Ulid? Id =>
        Ulid.TryParse(
            _httpContextAccessor.HttpContext?.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value,
            CultureInfo.InvariantCulture,
            out var id)
            ? id
            : null;

    public bool IsAuthenticated => Id is not null;

    public IReadOnlyCollection<string> Roles =>
        _httpContextAccessor.HttpContext?.User.FindAll("role").Select(static claim => claim.Value).ToArray() ?? [];

    public bool IsOwner(Ulid ownerId) => Id is { } id && id == ownerId;

    public Task<bool> HasPermissionAsync(
        string action, string scope = PermissionScopes.Global, Ulid? scopeId = null, CancellationToken cancellationToken = default)
    {
        if (Id is not { } id)
        {
            return Task.FromResult(false);
        }

        var context = _httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("Permission resolution requires an active request scope.");
        var permissions = context.RequestServices.GetRequiredService<IPermissionService>();
        return permissions.HasPermissionAsync(id, action, scope, scopeId, cancellationToken);
    }
}
