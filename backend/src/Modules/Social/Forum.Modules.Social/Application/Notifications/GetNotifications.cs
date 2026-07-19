using Forum.Common.Cqrs;
using Forum.Common.Paging;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.SharedKernel.Results;

using Microsoft.Extensions.Options;

namespace Forum.Modules.Social.Application.Notifications;

/// <summary>The caller's bell feed, newest first, keyset by notification id; optionally unread only.</summary>
internal sealed record GetNotificationsQuery(bool UnreadOnly, string? Cursor, int? Limit)
    : IQuery<CursorPage<NotificationResponse>>;

internal sealed class GetNotificationsQueryHandler
    : IQueryHandler<GetNotificationsQuery, CursorPage<NotificationResponse>>
{
    private readonly ISocialQueries _queries;
    private readonly ICurrentUser _currentUser;
    private readonly SocialOptions _options;

    public GetNotificationsQueryHandler(
        ISocialQueries queries, ICurrentUser currentUser, IOptions<SocialOptions> options)
    {
        _queries = queries;
        _currentUser = currentUser;
        _options = options.Value;
    }

    public async Task<Result<CursorPage<NotificationResponse>>> Handle(
        GetNotificationsQuery query, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure<CursorPage<NotificationResponse>>(SocialErrors.AuthenticationRequired);
        }

        if (!SocialCursors.TryParse(query.Cursor, out var cursor))
        {
            return Result.Failure<CursorPage<NotificationResponse>>(SocialErrors.InvalidCursor);
        }

        var limit = SocialCursors.ClampLimit(query.Limit, _options);
        return Result.Success(
            await _queries.GetNotificationsAsync(userId, query.UnreadOnly, cursor, limit, cancellationToken));
    }
}
