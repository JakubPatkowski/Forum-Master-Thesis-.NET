using Forum.Modules.Engagement.Application.Abstractions;
using Forum.Modules.Engagement.Domain.Reactions;

using Microsoft.EntityFrameworkCore;

namespace Forum.Modules.Engagement.Infrastructure.Persistence;

internal sealed class ReactionRepository : IReactionRepository
{
    private readonly EngagementDbContext _db;

    public ReactionRepository(EngagementDbContext db) => _db = db;

    public Task<Reaction?> GetAsync(
        Ulid userId, ReactionTargetType targetType, Ulid targetId, string reactionType,
        CancellationToken cancellationToken) =>
        _db.Reactions.AsTracking().FirstOrDefaultAsync(
            reaction => reaction.UserId == userId
                && reaction.TargetType == targetType
                && reaction.TargetId == targetId
                && reaction.ReactionType == reactionType,
            cancellationToken);

    public void Add(Reaction reaction) => _db.Reactions.Add(reaction);

    public void Remove(Reaction reaction) => _db.Reactions.Remove(reaction);

    public Task<int> DeleteAllForTargetAsync(
        ReactionTargetType targetType, Ulid targetId, CancellationToken cancellationToken) =>
        _db.Reactions
            .Where(reaction => reaction.TargetType == targetType && reaction.TargetId == targetId)
            .ExecuteDeleteAsync(cancellationToken);
}
