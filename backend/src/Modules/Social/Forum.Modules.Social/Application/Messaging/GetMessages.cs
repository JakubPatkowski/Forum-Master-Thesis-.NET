using Forum.Common.Cqrs;
using Forum.Common.Paging;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.SharedKernel.Results;

using Microsoft.Extensions.Options;

namespace Forum.Modules.Social.Application.Messaging;

/// <summary>
/// Message history, newest first, keyset by message id (a ULID id IS the creation order). Active participants
/// only — a kicked/left member loses history access with their seat. Tombstones ride along with the body already
/// masked by the view.
/// </summary>
internal sealed record GetMessagesQuery(Ulid ConversationId, string? Cursor, int? Limit)
    : IQuery<CursorPage<MessageResponse>>;

internal sealed class GetMessagesQueryHandler : IQueryHandler<GetMessagesQuery, CursorPage<MessageResponse>>
{
    private readonly ISocialQueries _queries;
    private readonly IConversationRepository _conversations;
    private readonly ICurrentUser _currentUser;
    private readonly SocialOptions _options;

    public GetMessagesQueryHandler(
        ISocialQueries queries,
        IConversationRepository conversations,
        ICurrentUser currentUser,
        IOptions<SocialOptions> options)
    {
        _queries = queries;
        _conversations = conversations;
        _currentUser = currentUser;
        _options = options.Value;
    }

    public async Task<Result<CursorPage<MessageResponse>>> Handle(
        GetMessagesQuery query, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure<CursorPage<MessageResponse>>(SocialErrors.AuthenticationRequired);
        }

        var conversation = await _conversations.GetByIdAsync(query.ConversationId, cancellationToken);
        if (conversation is null)
        {
            return Result.Failure<CursorPage<MessageResponse>>(SocialErrors.ConversationNotFound);
        }

        var seat = await _conversations.GetParticipantAsync(conversation.Id, userId, cancellationToken);
        if (seat is not { IsActive: true })
        {
            return Result.Failure<CursorPage<MessageResponse>>(SocialErrors.NotParticipant);
        }

        if (!SocialCursors.TryParse(query.Cursor, out var cursor))
        {
            return Result.Failure<CursorPage<MessageResponse>>(SocialErrors.InvalidCursor);
        }

        var limit = SocialCursors.ClampLimit(query.Limit, _options);
        return Result.Success(await _queries.GetMessagesAsync(conversation.Id, cursor, limit, cancellationToken));
    }
}
