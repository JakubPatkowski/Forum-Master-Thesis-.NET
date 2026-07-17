using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Notifications;

/// <summary>The bell badge number (cheap partial-index count; the SPA re-fetches it on every notification push).</summary>
internal sealed record GetUnreadNotificationCountQuery : IQuery<UnreadCountResponse>;

internal sealed record UnreadCountResponse(int Unread);

internal sealed class GetUnreadNotificationCountQueryHandler
    : IQueryHandler<GetUnreadNotificationCountQuery, UnreadCountResponse>
{
    private readonly ISocialQueries _queries;
    private readonly ICurrentUser _currentUser;

    public GetUnreadNotificationCountQueryHandler(ISocialQueries queries, ICurrentUser currentUser)
    {
        _queries = queries;
        _currentUser = currentUser;
    }

    public async Task<Result<UnreadCountResponse>> Handle(
        GetUnreadNotificationCountQuery query, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure<UnreadCountResponse>(SocialErrors.AuthenticationRequired);
        }

        return Result.Success(
            new UnreadCountResponse(await _queries.GetUnreadNotificationCountAsync(userId, cancellationToken)));
    }
}
