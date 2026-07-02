using Forum.Modules.Content.Domain.Comments;

namespace Forum.Modules.Content.Application.Abstractions;

internal interface ICommentRepository
{
    /// <summary>Tracked load for writes; the soft-delete filter hides deleted comments.</summary>
    Task<Comment?> GetByIdAsync(Ulid id, CancellationToken cancellationToken);

    void Add(Comment comment);
}
