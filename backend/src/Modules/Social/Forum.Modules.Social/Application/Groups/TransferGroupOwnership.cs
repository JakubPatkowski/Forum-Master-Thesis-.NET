using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Contracts.IntegrationEvents;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Groups;

/// <summary>
/// Transfers ownership (OWNER-only — stricter than the manage-gate: staff can moderate a group but not take or
/// give it away). The target must be a current member. The old owner stays a plain member (no automatic admin
/// grant — the new owner can promote them back); the new owner needs no grant, ownership itself outranks it.
/// </summary>
internal sealed record TransferGroupOwnershipCommand(Ulid GroupId, Ulid NewOwnerId) : ICommand;

internal sealed class TransferGroupOwnershipCommandHandler : ICommandHandler<TransferGroupOwnershipCommand>
{
    private readonly IGroupRepository _groups;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public TransferGroupOwnershipCommandHandler(
        IGroupRepository groups,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _groups = groups;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(TransferGroupOwnershipCommand command, CancellationToken cancellationToken)
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

        if (group.OwnerId != userId)
        {
            return Result.Failure(SocialErrors.GroupForbidden);
        }

        if (await _groups.GetMembershipAsync(group.Id, command.NewOwnerId, cancellationToken) is null)
        {
            return Result.Failure(SocialErrors.TransferTargetNotMember);
        }

        group.TransferOwnership(command.NewOwnerId);
        _outbox.Enqueue(new GroupUpdatedIntegrationEvent(Ulid.NewUlid(), group.Id, _clock.GetUtcNow()));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
