using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Identity.Application.Abstractions;
using Forum.Modules.Identity.Contracts.IntegrationEvents;
using Forum.Modules.Identity.Domain.Users;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Identity.Application.Administration;

/// <summary>Blocks or unblocks an account (admin). Blocking publishes <c>UserBlocked</c> for other modules.</summary>
internal sealed record SetUserStatusCommand(Ulid TargetUserId, bool Block) : ICommand;

internal sealed class SetUserStatusCommandHandler : ICommandHandler<SetUserStatusCommand>
{
    private readonly IUserRepository _users;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _clock;

    public SetUserStatusCommandHandler(
        IUserRepository users, IOutboxWriter outbox, IUnitOfWork unitOfWork, ICurrentUser currentUser, TimeProvider clock)
    {
        _users = users;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<Result> Handle(SetUserStatusCommand command, CancellationToken cancellationToken)
    {
        var user = await _users.GetByIdAsync(command.TargetUserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(UserErrors.NotFound);
        }

        var actor = _currentUser.Id ?? Ulid.Empty;

        if (command.Block)
        {
            var result = user.Block(actor);
            if (result.IsFailure)
            {
                return result;
            }

            _outbox.Enqueue(new UserBlockedIntegrationEvent(Ulid.NewUlid(), user.Id, actor, _clock.GetUtcNow()));
        }
        else
        {
            var result = user.Unblock();
            if (result.IsFailure)
            {
                return result;
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
