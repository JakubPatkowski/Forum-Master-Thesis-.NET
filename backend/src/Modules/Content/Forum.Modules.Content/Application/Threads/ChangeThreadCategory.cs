using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Contracts.IntegrationEvents;
using Forum.Modules.Content.Domain.Categories;
using Forum.Modules.Content.Domain.Threads;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Threads;

/// <summary>Moves a thread to another category. Requires <c>moderate</c> at the thread's current category.</summary>
internal sealed record ChangeThreadCategoryCommand(Ulid ThreadId, Ulid CategoryId) : ICommand;

internal sealed class ChangeThreadCategoryCommandHandler : ICommandHandler<ChangeThreadCategoryCommand>
{
    private readonly IThreadRepository _threads;
    private readonly ICategoryRepository _categories;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public ChangeThreadCategoryCommandHandler(
        IThreadRepository threads,
        ICategoryRepository categories,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _threads = threads;
        _categories = categories;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(ChangeThreadCategoryCommand command, CancellationToken cancellationToken)
    {
        var thread = await _threads.GetByIdAsync(command.ThreadId, cancellationToken);
        if (thread is null)
        {
            return Result.Failure(ThreadErrors.NotFound);
        }

        var target = await _categories.GetByIdAsync(command.CategoryId, cancellationToken);
        if (target is null)
        {
            return Result.Failure(CategoryErrors.NotFound);
        }

        if (!await _currentUser.IsModeratorOfAsync(thread.CategoryId, cancellationToken))
        {
            return Result.Failure(ThreadErrors.ModerationRequired);
        }

        var result = thread.ChangeCategory(target.Id);
        if (result.IsFailure)
        {
            return result;
        }

        _outbox.Enqueue(new ThreadUpdatedIntegrationEvent(Ulid.NewUlid(), thread.Id, _clock.GetUtcNow()));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
