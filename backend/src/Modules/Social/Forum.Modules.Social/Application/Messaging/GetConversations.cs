using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.SharedKernel.Results;

using Microsoft.Extensions.Options;

namespace Forum.Modules.Social.Application.Messaging;

/// <summary>
/// The caller's conversation list, last activity first, with preview + unread count from the view. Hard-capped
/// instead of keyset-paged — last-activity order is unstable under a cursor, and the list is naturally bounded
/// (friends + groups), which is the module's one documented paging exception.
/// </summary>
internal sealed record GetConversationsQuery : IQuery<IReadOnlyList<ConversationResponse>>;

internal sealed class GetConversationsQueryHandler
    : IQueryHandler<GetConversationsQuery, IReadOnlyList<ConversationResponse>>
{
    private readonly ISocialQueries _queries;
    private readonly ICurrentUser _currentUser;
    private readonly SocialOptions _options;

    public GetConversationsQueryHandler(
        ISocialQueries queries, ICurrentUser currentUser, IOptions<SocialOptions> options)
    {
        _queries = queries;
        _currentUser = currentUser;
        _options = options.Value;
    }

    public async Task<Result<IReadOnlyList<ConversationResponse>>> Handle(
        GetConversationsQuery query, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure<IReadOnlyList<ConversationResponse>>(SocialErrors.AuthenticationRequired);
        }

        return Result.Success(
            await _queries.GetConversationsAsync(userId, _options.MaxConversationsListed, cancellationToken));
    }
}
