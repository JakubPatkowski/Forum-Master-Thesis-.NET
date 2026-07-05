using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Engagement.Application.Abstractions;
using Forum.Modules.Engagement.Contracts.IntegrationEvents;
using Forum.Modules.Engagement.Domain.Reactions;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Engagement.Application.Reactions;

/// <summary>
/// Ensures the current user's 'like' exists on the target. Idempotent: re-liking is a no-op that still succeeds
/// with the current summary (no second row, no second event). Gate mirrors Content's CreateThread: target
/// 404 → private-category 403 → <c>like</c> permission 403, resolved at the target's category scope so
/// per-category ACL denies apply.
/// </summary>
internal sealed record AddReactionCommand(ReactionTargetType TargetType, Ulid TargetId)
    : ICommand<ReactionSummaryResponse>;

internal sealed class AddReactionCommandHandler : ICommandHandler<AddReactionCommand, ReactionSummaryResponse>
{
    private readonly IReactionTargetReader _targets;
    private readonly IReactionRepository _reactions;
    private readonly IEngagementQueries _queries;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public AddReactionCommandHandler(
        IReactionTargetReader targets,
        IReactionRepository reactions,
        IEngagementQueries queries,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _targets = targets;
        _reactions = reactions;
        _queries = queries;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<ReactionSummaryResponse>> Handle(
        AddReactionCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure<ReactionSummaryResponse>(EngagementErrors.AuthenticationRequired);
        }

        var target = await _targets.GetAsync(command.TargetType, command.TargetId, cancellationToken);
        if (target is null)
        {
            return Result.Failure<ReactionSummaryResponse>(EngagementErrors.TargetNotFound);
        }

        if (target.CategoryIsPrivate && !await _currentUser.MaySeePrivateCategoryAsync(target, cancellationToken))
        {
            return Result.Failure<ReactionSummaryResponse>(EngagementErrors.PrivateCategory);
        }

        if (!await _currentUser.HasPermissionAsync(
                Permissions.Like, PermissionScopes.Category, target.CategoryId, cancellationToken))
        {
            return Result.Failure<ReactionSummaryResponse>(EngagementErrors.LikeForbidden);
        }

        var existing = await _reactions.GetAsync(
            userId, command.TargetType, command.TargetId, ReactionTypes.Like, cancellationToken);
        if (existing is null)
        {
            var now = _clock.GetUtcNow();
            _reactions.Add(new Reaction(userId, command.TargetType, command.TargetId, ReactionTypes.Like, now));
            _outbox.Enqueue(new ReactionAddedIntegrationEvent(
                Ulid.NewUlid(), userId, ReactionTargets.ToWire(command.TargetType), command.TargetId,
                ReactionTypes.Like, target.CategoryId, target.ThreadId, now));
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(
            await _queries.GetSummaryAsync(command.TargetType, command.TargetId, userId, cancellationToken));
    }
}
