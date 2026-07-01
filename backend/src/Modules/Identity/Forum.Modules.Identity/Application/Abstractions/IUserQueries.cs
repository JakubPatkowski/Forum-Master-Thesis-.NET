using Forum.Common.Paging;
using Forum.Modules.Identity.Application.Administration;

namespace Forum.Modules.Identity.Application.Abstractions;

/// <summary>Read-side port for user lists (keyset paged by ULID id; no-tracking projection).</summary>
internal interface IUserQueries
{
    Task<CursorPage<UserSummaryResponse>> ListAsync(string? cursor, int limit, CancellationToken cancellationToken);
}
