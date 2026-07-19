using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Contracts.IntegrationEvents;
using Forum.Modules.Social.Domain.Conversations;
using Forum.Modules.Social.Domain.Groups;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Groups;

/// <summary>
/// Joins a PUBLIC group without an invite (private ones read 403 — invitation only). Idempotent for existing
/// members. Membership and the chat seat commit together; a returning member's old seat is re-activated so their
/// history attribution survives.
/// </summary>
internal sealed record JoinGroupCommand(Ulid GroupId) : ICommand;

internal sealed class JoinGroupCommandHandler : ICommandHandler<JoinGroupCommand>
{
    private readonly IGroupRepository _groups;
    private readonly IConversationRepository _conversations;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public JoinGroupCommandHandler(
        IGroupRepository groups,
        IConversationRepository conversations,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _groups = groups;
        _conversations = conversations;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(JoinGroupCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure(SocialErrors.AuthenticationRequired);
        }

        var group = await _groups.GetByIdAsync(command.GroupId, cancellationToken);
        if (group is null)
        {
            return Result.Failure(SocialErrors.GroupNotFound);
        }

        if (group.Visibility != GroupVisibility.Public)
        {
            return Result.Failure(SocialErrors.GroupPrivate);
        }

        if (await _groups.GetMembershipAsync(group.Id, userId, cancellationToken) is not null)
        {
            return Result.Success(); // Idempotent: already a member.
        }

        var now = _clock.GetUtcNow();
        await GroupMembershipWriter.AddMemberAsync(
            _groups, _conversations, group.Id, userId, invitedBy: null, now, cancellationToken);
        _outbox.Enqueue(new GroupMemberJoinedIntegrationEvent(Ulid.NewUlid(), group.Id, userId, now));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
