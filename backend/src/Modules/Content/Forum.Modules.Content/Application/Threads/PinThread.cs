using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Contracts.IntegrationEvents;
using Forum.Modules.Content.Domain.Threads;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Threads;

/// <summary>Pins or unpins a thread in its category's feed. Requires <c>moderate</c> at the category scope.</summary>
internal sealed record PinThreadCommand(Ulid ThreadId, bool Pinned) : ICommand;

internal sealed class PinThreadCommandHandler : ICommandHandler<PinThreadCommand>
{
    private readonly IThreadRepository _threads;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public PinThreadCommandHandler(
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

    public async Task<Result> Handle(PinThreadCommand command, CancellationToken cancellationToken)
    {
        var thread = await _threads.GetByIdAsync(command.ThreadId, cancellationToken);
        if (thread is null)
        {
            return Result.Failure(ThreadErrors.NotFound);
        }

        if (!await _currentUser.IsModeratorOfAsync(thread.CategoryId, cancellationToken))
        {
            return Result.Failure(ThreadErrors.ModerationRequired);
        }

        if (command.Pinned)
        {
            thread.Pin();
        }
        else
        {
            thread.Unpin();
        }

        _outbox.Enqueue(new ThreadUpdatedIntegrationEvent(
            Ulid.NewUlid(), thread.Id, thread.CategoryId, _clock.GetUtcNow()));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
