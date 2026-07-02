using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Domain.Comments;

using Microsoft.EntityFrameworkCore;

namespace Forum.Modules.Content.Infrastructure.Persistence;

internal sealed class CommentRepository : ICommentRepository
{
    private readonly ContentDbContext _db;

    public CommentRepository(ContentDbContext db) => _db = db;

    public Task<Comment?> GetByIdAsync(Ulid id, CancellationToken cancellationToken) =>
        _db.Comments.AsTracking().FirstOrDefaultAsync(comment => comment.Id == id, cancellationToken);

    public void Add(Comment comment) => _db.Comments.Add(comment);
}
