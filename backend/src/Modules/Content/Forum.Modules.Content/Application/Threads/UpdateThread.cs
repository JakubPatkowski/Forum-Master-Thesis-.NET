using FluentValidation;

using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Application.Validation;
using Forum.Modules.Content.Contracts.IntegrationEvents;
using Forum.Modules.Content.Domain.Threads;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Threads;

/// <summary>Replaces a thread's title and body. Allowed for the author or a moderator.</summary>
internal sealed record UpdateThreadCommand(Ulid ThreadId, string Title, string Body) : ICommand;

internal sealed class UpdateThreadCommandValidator : AbstractValidator<UpdateThreadCommand>
{
    public UpdateThreadCommandValidator()
    {
        RuleFor(static command => command.Title).NotEmpty().Length(3, 200);
        RuleFor(static command => command.Body).NotEmpty().MaximumLength(50_000);
    }
}

internal sealed class UpdateThreadCommandHandler : ICommandHandler<UpdateThreadCommand>
{
    private readonly IValidator<UpdateThreadCommand> _validator;
    private readonly IThreadRepository _threads;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public UpdateThreadCommandHandler(
        IValidator<UpdateThreadCommand> validator,
        IThreadRepository threads,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _validator = validator;
        _threads = threads;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(UpdateThreadCommand command, CancellationToken cancellationToken)
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

        if (await _validator.ValidateToErrorAsync(command, cancellationToken) is { } validationError)
        {
            return Result.Failure(validationError);
        }

        thread.Update(command.Title, command.Body);
        _outbox.Enqueue(new ThreadUpdatedIntegrationEvent(
            Ulid.NewUlid(), thread.Id, thread.CategoryId, _clock.GetUtcNow()));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
