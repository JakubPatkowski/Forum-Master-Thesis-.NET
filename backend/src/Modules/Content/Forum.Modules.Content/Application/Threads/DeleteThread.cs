using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Contracts.IntegrationEvents;
using Forum.Modules.Content.Domain.Threads;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Threads;

/// <summary>Soft-deletes a thread. Allowed for the author or a moderator; comments stay in place.</summary>
internal sealed record DeleteThreadCommand(Ulid ThreadId) : ICommand;

internal sealed class DeleteThreadCommandHandler : ICommandHandler<DeleteThreadCommand>
{
    private readonly IThreadRepository _threads;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public DeleteThreadCommandHandler(
        IThreadRepository threads,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _threads = threads;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(DeleteThreadCommand command, CancellationToken cancellationToken)
    {
        var thread = await _threads.GetByIdAsync(command.ThreadId, cancellationToken);
        if (thread is null)
        {
            return Result.Failure(ThreadErrors.NotFound);
        }

        if (!await _currentUser.IsOwnerOrModeratorAsync(thread.OwnerId, thread.CategoryId, cancellationToken))
        {
            return Result.Failure(ThreadErrors.NotOwnerNorModerator);
        }

        var now = _clock.GetUtcNow();
        var result = thread.Delete(_currentUser.Id ?? Ulid.Empty, now);
        if (result.IsFailure)
        {
            return result;
        }

        _outbox.Enqueue(new ThreadDeletedIntegrationEvent(Ulid.NewUlid(), thread.Id, now));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
