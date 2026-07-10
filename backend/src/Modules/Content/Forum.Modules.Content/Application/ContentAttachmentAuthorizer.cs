using Forum.Common.Security;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Contracts;
using Forum.Modules.Content.Domain.Categories;
using Forum.Modules.Content.Domain.Comments;
using Forum.Modules.Content.Domain.Threads;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application;

/// <summary>
/// Implements <see cref="IContentAuthorization"/> with the exact gates Content's own write handlers use:
/// the target must exist (404) and the user must be its owner or hold <c>moderate</c> at the category scope (403).
/// Takes an explicit user id (not <see cref="ICurrentUser"/>) so the verdict is caller-independent.
/// </summary>
internal sealed class ContentAttachmentAuthorizer : IContentAuthorization
{
    private readonly ICategoryRepository _categories;
    private readonly IThreadRepository _threads;
    private readonly ICommentRepository _comments;
    private readonly IPermissionService _permissions;

    public ContentAttachmentAuthorizer(
        ICategoryRepository categories,
        IThreadRepository threads,
        ICommentRepository comments,
        IPermissionService permissions)
    {
        _categories = categories;
        _threads = threads;
        _comments = comments;
        _permissions = permissions;
    }

    public Task<Result> AuthorizeAttachmentAsync(
        ContentAttachmentTarget target, Ulid targetId, Ulid userId, CancellationToken cancellationToken) =>
        target switch
        {
            ContentAttachmentTarget.Thread => AuthorizeThreadAsync(targetId, userId, cancellationToken),
            // A thread's icon is gated exactly like any other attachment on the thread (owner-or-moderator).
            ContentAttachmentTarget.ThreadIcon => AuthorizeThreadAsync(targetId, userId, cancellationToken),
            ContentAttachmentTarget.Comment => AuthorizeCommentAsync(targetId, userId, cancellationToken),
            ContentAttachmentTarget.CategoryIcon => AuthorizeCategoryAsync(targetId, userId, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown attachment target."),
        };

    private async Task<Result> AuthorizeThreadAsync(Ulid threadId, Ulid userId, CancellationToken cancellationToken)
    {
        var thread = await _threads.GetByIdAsync(threadId, cancellationToken);
        if (thread is null)
        {
            return Result.Failure(ThreadErrors.NotFound);
        }

        return await IsOwnerOrModeratorAsync(userId, thread.OwnerId, thread.CategoryId, cancellationToken)
            ? Result.Success()
            : Result.Failure(ThreadErrors.NotOwnerNorModerator);
    }

    private async Task<Result> AuthorizeCommentAsync(Ulid commentId, Ulid userId, CancellationToken cancellationToken)
    {
        var comment = await _comments.GetByIdAsync(commentId, cancellationToken);
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

        return await IsOwnerOrModeratorAsync(userId, comment.OwnerId, thread.CategoryId, cancellationToken)
            ? Result.Success()
            : Result.Failure(CommentErrors.NotOwnerNorModerator);
    }

    private async Task<Result> AuthorizeCategoryAsync(Ulid categoryId, Ulid userId, CancellationToken cancellationToken)
    {
        var category = await _categories.GetByIdAsync(categoryId, cancellationToken);
        if (category is null)
        {
            return Result.Failure(CategoryErrors.NotFound);
        }

        return await IsOwnerOrModeratorAsync(userId, category.OwnerId, category.Id, cancellationToken)
            ? Result.Success()
            : Result.Failure(CategoryErrors.NotOwnerNorModerator);
    }

    private async Task<bool> IsOwnerOrModeratorAsync(
        Ulid userId, Ulid ownerId, Ulid categoryId, CancellationToken cancellationToken) =>
        userId == ownerId
        || await _permissions.HasPermissionAsync(
            userId, Permissions.Moderate, PermissionScopes.Category, categoryId, cancellationToken);
}
