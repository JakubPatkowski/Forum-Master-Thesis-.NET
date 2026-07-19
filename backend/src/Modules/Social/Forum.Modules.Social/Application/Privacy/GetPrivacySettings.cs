using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Domain.Privacy;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Privacy;

/// <summary>The caller's privacy settings; a missing row reads as the defaults (everyone/everyone/everyone/true).</summary>
internal sealed record GetPrivacySettingsQuery : IQuery<PrivacySettingsResponse>;

internal sealed record PrivacySettingsResponse(
    string FriendRequests, string Messages, string GroupInvites, bool ShowOnlineStatus);

internal sealed class GetPrivacySettingsQueryHandler
    : IQueryHandler<GetPrivacySettingsQuery, PrivacySettingsResponse>
{
    private readonly IPrivacySettingsRepository _privacy;
    private readonly ICurrentUser _currentUser;

    public GetPrivacySettingsQueryHandler(IPrivacySettingsRepository privacy, ICurrentUser currentUser)
    {
        _privacy = privacy;
        _currentUser = currentUser;
    }

    public async Task<Result<PrivacySettingsResponse>> Handle(
        GetPrivacySettingsQuery query, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure<PrivacySettingsResponse>(SocialErrors.AuthenticationRequired);
        }

        var settings = await _privacy.GetAsync(userId, cancellationToken) ?? new UserPrivacySettings(userId);
        return Result.Success(new PrivacySettingsResponse(
            PrivacyWire.ToWire(settings.FriendRequests),
            PrivacyWire.ToWire(settings.Messages),
            PrivacyWire.ToWire(settings.GroupInvites),
            settings.ShowOnlineStatus));
    }
}
