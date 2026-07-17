using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Domain.Privacy;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Privacy;

/// <summary>
/// Upserts the caller's privacy row (created lazily on first change). 'friends' for friend requests normalizes
/// to 'no_one' in the domain — friends by definition need no request.
/// </summary>
internal sealed record UpdatePrivacySettingsCommand(
    string FriendRequests, string Messages, string GroupInvites, bool ShowOnlineStatus) : ICommand;

internal sealed class UpdatePrivacySettingsCommandHandler : ICommandHandler<UpdatePrivacySettingsCommand>
{
    private readonly IPrivacySettingsRepository _privacy;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;

    public UpdatePrivacySettingsCommandHandler(
        IPrivacySettingsRepository privacy, ICurrentUser currentUser, IUnitOfWork unitOfWork)
    {
        _privacy = privacy;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdatePrivacySettingsCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure(SocialErrors.AuthenticationRequired);
        }

        if (!PrivacyWire.TryParse(command.FriendRequests, out var friendRequests)
            || !PrivacyWire.TryParse(command.Messages, out var messages)
            || !PrivacyWire.TryParse(command.GroupInvites, out var groupInvites))
        {
            return Result.Failure(SocialErrors.UnknownAudience);
        }

        var settings = await _privacy.GetAsync(userId, cancellationToken);
        if (settings is null)
        {
            settings = new UserPrivacySettings(userId);
            _privacy.Add(settings);
        }

        settings.Update(friendRequests, messages, groupInvites, command.ShowOnlineStatus);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
