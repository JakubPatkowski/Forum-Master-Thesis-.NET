using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Notifications;

/// <summary>Marks the given notifications read — or ALL unread when no ids are sent. Always scoped to the caller.</summary>
internal sealed record MarkNotificationsReadCommand(IReadOnlyList<Ulid>? Ids) : ICommand<MarkNotificationsReadResponse>;

internal sealed record MarkNotificationsReadResponse(int Marked);

internal sealed class MarkNotificationsReadCommandHandler
    : ICommandHandler<MarkNotificationsReadCommand, MarkNotificationsReadResponse>
{
    private readonly INotificationRepository _notifications;
    private readonly ICurrentUser _currentUser;

    public MarkNotificationsReadCommandHandler(INotificationRepository notifications, ICurrentUser currentUser)
    {
        _notifications = notifications;
        _currentUser = currentUser;
    }

    public async Task<Result<MarkNotificationsReadResponse>> Handle(
        MarkNotificationsReadCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure<MarkNotificationsReadResponse>(SocialErrors.AuthenticationRequired);
        }

        var marked = await _notifications.MarkReadAsync(userId, command.Ids, cancellationToken);
        return Result.Success(new MarkNotificationsReadResponse(marked));
    }
}
