using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Contracts.IntegrationEvents;
using Forum.Modules.Content.Domain.Comments;
using Forum.Modules.Content.Domain.Threads;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Comments;

/// <summary>
/// Soft-deletes a comment: the body becomes <c>"[deleted]"</c> and the children stay attached.
/// Allowed for the author or a moderator of the thread's category.
/// </summary>
internal sealed record DeleteCommentCommand(Ulid CommentId) : ICommand;

internal sealed class DeleteCommentCommandHandler : ICommandHandler<DeleteCommentCommand>
{
    private readonly ICommentRepository _comments;
    private readonly IThreadRepository _threads;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public DeleteCommentCommandHandler(
        ICommentRepository comments,
        IThreadRepository threads,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _comments = comments;
        _threads = threads;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(DeleteCommentCommand command, CancellationToken cancellationToken)
    {
        var comment = await _comments.GetByIdAsync(command.CommentId, cancellationToken);
        if (comment is null)
        {
            return Result.Failure(CommentErrors.NotFound);
        }

        var thread = await _threads.GetByIdAsync(comment.ThreadId, cancellationToken);
        if (thread is null)
        {
            return Result.Failure(ThreadErrors.NotFound);
        }

        if (!await _currentUser.IsOwnerOrModeratorAsync(comment.OwnerId, thread.CategoryId, cancellationToken))
        {
            return Result.Failure(CommentErrors.NotOwnerNorModerator);
        }

        var now = _clock.GetUtcNow();
        var result = comment.Delete(_currentUser.Id ?? Ulid.Empty, now);
        if (result.IsFailure)
        {
            return result;
        }

        _outbox.Enqueue(new CommentDeletedIntegrationEvent(
            Ulid.NewUlid(), comment.Id, comment.ThreadId, thread.CategoryId, now));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
