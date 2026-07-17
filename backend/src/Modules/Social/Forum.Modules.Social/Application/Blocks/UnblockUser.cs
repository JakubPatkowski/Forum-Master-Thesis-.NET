using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Blocks;

/// <summary>DELETE-idempotent unblock (removing a non-existent block still succeeds).</summary>
internal sealed record UnblockUserCommand(Ulid BlockedId) : ICommand;

internal sealed class UnblockUserCommandHandler : ICommandHandler<UnblockUserCommand>
{
    private readonly ISocialBlockRepository _blocks;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;

    public UnblockUserCommandHandler(ISocialBlockRepository blocks, ICurrentUser currentUser, IUnitOfWork unitOfWork)
    {
        _blocks = blocks;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UnblockUserCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure(SocialErrors.AuthenticationRequired);
        }

        var block = await _blocks.GetAsync(userId, command.BlockedId, cancellationToken);
        if (block is null)
        {
            return Result.Success();
        }

        _blocks.Remove(block);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
