using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.SharedKernel.Results;

using Microsoft.Extensions.Options;

namespace Forum.Modules.Social.Application.Presence;

/// <summary>
/// Batch presence lookup (one round trip, Engagement's reaction-batch precedent; ≤ the configured cap). Any
/// authenticated user may ask; users hiding their status — and unknown ids — read as offline, so the response
/// never reveals whether an id exists or hides.
/// </summary>
internal sealed record GetPresenceQuery(IReadOnlyList<Ulid> UserIds) : IQuery<IReadOnlyList<PresenceEntryResponse>>;

internal sealed record PresenceEntryResponse(Ulid UserId, string Status);

internal sealed class GetPresenceQueryHandler : IQueryHandler<GetPresenceQuery, IReadOnlyList<PresenceEntryResponse>>
{
    private readonly IPresenceStore _presence;
    private readonly ICurrentUser _currentUser;
    private readonly SocialOptions _options;

    public GetPresenceQueryHandler(IPresenceStore presence, ICurrentUser currentUser, IOptions<SocialOptions> options)
    {
        _presence = presence;
        _currentUser = currentUser;
        _options = options.Value;
    }

    public async Task<Result<IReadOnlyList<PresenceEntryResponse>>> Handle(
        GetPresenceQuery query, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is null)
        {
            return Result.Failure<IReadOnlyList<PresenceEntryResponse>>(SocialErrors.AuthenticationRequired);
        }

        if (query.UserIds.Count > _options.MaxPresenceBatch)
        {
            return Result.Failure<IReadOnlyList<PresenceEntryResponse>>(SocialErrors.TooManyPresenceIds);
        }

        var statuses = await _presence.GetStatusesAsync(query.UserIds, cancellationToken);
        var entries = query.UserIds
            .Distinct()
            .Select(id => new PresenceEntryResponse(
                id, statuses.TryGetValue(id, out var status) ? status.ToString().ToLowerInvariant() : "offline"))
            .ToArray();
        return Result.Success<IReadOnlyList<PresenceEntryResponse>>(entries);
    }
}
