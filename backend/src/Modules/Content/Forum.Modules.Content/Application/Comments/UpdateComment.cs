using FluentValidation;

using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Application.Validation;
using Forum.Modules.Content.Domain.Comments;
using Forum.Modules.Content.Domain.Threads;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Comments;

/// <summary>Replaces a comment's body. Allowed for the author or a moderator of the thread's category.</summary>
internal sealed record UpdateCommentCommand(Ulid CommentId, string Body) : ICommand;

internal sealed class UpdateCommentCommandValidator : AbstractValidator<UpdateCommentCommand>
{
    public UpdateCommentCommandValidator() =>
        RuleFor(static command => command.Body).NotEmpty().MaximumLength(10_000);
}

internal sealed class UpdateCommentCommandHandler : ICommandHandler<UpdateCommentCommand>
{
    private readonly IValidator<UpdateCommentCommand> _validator;
    private readonly ICommentRepository _comments;
    private readonly IThreadRepository _threads;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateCommentCommandHandler(
        IValidator<UpdateCommentCommand> validator,
        ICommentRepository comments,
        IThreadRepository threads,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork)
    {
        _validator = validator;
        _comments = comments;
        _threads = threads;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateCommentCommand command, CancellationToken cancellationToken)
    {
        var comment = await _comments.GetByIdAsync(command.CommentId, cancellationToken);
        if (comment is null)
        {
            return Result.Failure(CommentErrors.NotFound);
        }

        // The thread carries the category the moderate permission is scoped to; a deleted thread 404s.
        var thread = await _threads.GetByIdAsync(comment.ThreadId, cancellationToken);
        if (thread is null)
        {
            return Result.Failure(ThreadErrors.NotFound);
        }

        if (!await _currentUser.IsOwnerOrModeratorAsync(comment.OwnerId, thread.CategoryId, cancellationToken))
        {
            return Result.Failure(CommentErrors.NotOwnerNorModerator);
        }

        if (await _validator.ValidateToErrorAsync(command, cancellationToken) is { } validationError)
        {
            return Result.Failure(validationError);
        }

        comment.Update(command.Body);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
