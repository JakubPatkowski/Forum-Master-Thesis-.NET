using System.Globalization;

using Forum.Common.Cqrs;
using Forum.Common.Http;
using Forum.Common.Modules;
using Forum.Modules.Social.Application.Presence;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Forum.Modules.Social.Presentation.Presence;

/// <summary>Batch lookup: <c>?userIds=id,id,...</c> (comma-separated, Engagement's batch precedent).</summary>
internal sealed class GetPresenceEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder app) =>
        app.MapGet("/api/social/presence", static async (
                string? userIds,
                IQueryHandler<GetPresenceQuery, IReadOnlyList<PresenceEntryResponse>> handler,
                CancellationToken cancellationToken) =>
            {
                var raw = (userIds ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var ids = new List<Ulid>(raw.Length);
                foreach (var value in raw)
                {
                    if (!Ulid.TryParse(value, CultureInfo.InvariantCulture, out var id))
                    {
                        return Results.NotFound();
                    }

                    ids.Add(id);
                }

                var result = await handler.Handle(new GetPresenceQuery(ids), cancellationToken);
                return result.Match(static entries => Results.Ok(entries));
            })
            .RequireAuthorization()
            .WithName("GetPresence")
            .WithTags("Social");
}
