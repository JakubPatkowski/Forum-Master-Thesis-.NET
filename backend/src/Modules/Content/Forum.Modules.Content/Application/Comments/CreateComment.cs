using FluentValidation;

using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Application.Validation;
using Forum.Modules.Content.Contracts.IntegrationEvents;
using Forum.Modules.Content.Domain.Comments;
using Forum.Modules.Content.Domain.Threads;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Comments;

/// <summary>
/// Adds a comment: top-level when <c>ParentId</c> is null, otherwise a reply extending the parent's materialized
/// path (depth capped at <see cref="Comment.MaxDepth"/>). Order: thread/parent 404 → permission 403 → input 422.
/// </summary>
internal sealed record CreateCommentCommand(Ulid ThreadId, Ulid? ParentId, string Body) : ICommand<CreateCommentResponse>;

internal sealed record CreateCommentResponse(Ulid CommentId);

internal sealed class CreateCommentCommandValidator : AbstractValidator<CreateCommentCommand>
{
    public CreateCommentCommandValidator() =>
        RuleFor(static command => command.Body).NotEmpty().MaximumLength(10_000);
}

internal sealed class CreateCommentCommandHandler : ICommandHandler<CreateCommentCommand, CreateCommentResponse>
{
    private readonly IValidator<CreateCommentCommand> _validator;
    private readonly IThreadRepository _threads;
    private readonly ICommentRepository _comments;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public CreateCommentCommandHandler(
        IValidator<CreateCommentCommand> validator,
        IThreadRepository threads,
        ICommentRepository comments,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _validator = validator;
        _threads = threads;
        _comments = comments;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<CreateCommentResponse>> Handle(CreateCommentCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } ownerId)
        {
            return Result.Failure<CreateCommentResponse>(ContentErrors.AuthenticationRequired);
        }

        // The soft-delete filter hides deleted threads, so commenting on one 404s here.
        var thread = await _threads.GetByIdAsync(command.ThreadId, cancellationToken);
        if (thread is null)
        {
            return Result.Failure<CreateCommentResponse>(ThreadErrors.NotFound);
        }

        Comment? parent = null;
        if (command.ParentId is { } parentId)
        {
            parent = await _comments.GetByIdAsync(parentId, cancellationToken);
            if (parent is null)
            {
                return Result.Failure<CreateCommentResponse>(CommentErrors.ParentNotFound);
            }

            if (parent.ThreadId != thread.Id)
            {
                return Result.Failure<CreateCommentResponse>(CommentErrors.ParentInDifferentThread);
            }
        }

        if (!await _currentUser.HasPermissionAsync(
                Permissions.Comment, PermissionScopes.Category, thread.CategoryId, cancellationToken))
        {
            return Result.Failure<CreateCommentResponse>(CommentErrors.CommentForbidden);
        }

        if (await _validator.ValidateToErrorAsync(command, cancellationToken) is { } validationError)
        {
            return Result.Failure<CreateCommentResponse>(validationError);
        }

        Comment comment;
        if (parent is null)
        {
            comment = Comment.CreateRoot(thread.Id, ownerId, command.Body);
        }
        else
        {
            var reply = Comment.CreateReply(parent, ownerId, command.Body);
            if (reply.IsFailure)
            {
                return Result.Failure<CreateCommentResponse>(reply.Error);
            }

            comment = reply.Value;
        }

        _comments.Add(comment);
        _outbox.Enqueue(new CommentCreatedIntegrationEvent(
            Ulid.NewUlid(), comment.Id, thread.Id, comment.ParentId, ownerId, thread.CategoryId, _clock.GetUtcNow()));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateCommentResponse(comment.Id));
    }
}
