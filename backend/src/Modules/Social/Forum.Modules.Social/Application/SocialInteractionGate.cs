using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Domain.Friendships;
using Forum.Modules.Social.Domain.Privacy;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application;

/// <summary>
/// The one place the block + privacy gates live, so every interaction denies identically. A block (either
/// direction) and a privacy setting return the SAME generic Forbidden error per interaction kind — on the wire a
/// block is indistinguishable from "does not accept requests", which is the point.
/// </summary>
internal sealed class SocialInteractionGate
{
    private readonly ISocialBlockRepository _blocks;
    private readonly IPrivacySettingsRepository _privacy;
    private readonly IFriendshipRepository _friendships;

    public SocialInteractionGate(
        ISocialBlockRepository blocks, IPrivacySettingsRepository privacy, IFriendshipRepository friendships)
    {
        _blocks = blocks;
        _privacy = privacy;
        _friendships = friendships;
    }

    public Task<Result> MayFriendRequestAsync(Ulid requesterId, Ulid addresseeId, CancellationToken cancellationToken) =>
        EvaluateAsync(
            requesterId, addresseeId, static settings => settings.FriendRequests,
            SocialErrors.FriendRequestNotAllowed, cancellationToken);

    public Task<Result> MayMessageAsync(Ulid senderId, Ulid recipientId, CancellationToken cancellationToken) =>
        EvaluateAsync(
            senderId, recipientId, static settings => settings.Messages,
            SocialErrors.MessageNotAllowed, cancellationToken);

    public Task<Result> MayInviteToGroupAsync(Ulid inviterId, Ulid inviteeId, CancellationToken cancellationToken) =>
        EvaluateAsync(
            inviterId, inviteeId, static settings => settings.GroupInvites,
            SocialErrors.GroupInviteNotAllowed, cancellationToken);

    private async Task<Result> EvaluateAsync(
        Ulid actorId,
        Ulid targetId,
        Func<UserPrivacySettings, PrivacyAudience> audienceOf,
        Error denied,
        CancellationToken cancellationToken)
    {
        if (await _blocks.AnyBetweenAsync(actorId, targetId, cancellationToken))
        {
            return Result.Failure(denied);
        }

        var settings = await _privacy.GetAsync(targetId, cancellationToken);
        var audience = settings is null ? PrivacyAudience.Everyone : audienceOf(settings);
        return audience switch
        {
            PrivacyAudience.Everyone => Result.Success(),
            PrivacyAudience.Friends => await AreFriendsAsync(actorId, targetId, cancellationToken)
                ? Result.Success()
                : Result.Failure(denied),
            _ => Result.Failure(denied),
        };
    }

    private async Task<bool> AreFriendsAsync(Ulid userA, Ulid userB, CancellationToken cancellationToken) =>
        await _friendships.GetBetweenAsync(userA, userB, cancellationToken) is { Status: FriendshipStatus.Accepted };
}
