using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Engagement.Application.Abstractions;
using Forum.Modules.Engagement.Contracts.IntegrationEvents;
using Forum.Modules.Engagement.Domain.Reactions;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Engagement.Application.Reactions;

/// <summary>
/// Ensures the current user's 'like' is absent from the target. Idempotent: un-liking something never liked is
/// a no-op that still succeeds with the current summary. Same gate shape as <see cref="AddReactionCommand"/>
/// (target 404 → private-category 403 → <c>like</c> permission 403) so both toggle directions behave alike.
/// </summary>
internal sealed record RemoveReactionCommand(ReactionTargetType TargetType, Ulid TargetId)
    : ICommand<ReactionSummaryResponse>;

internal sealed class RemoveReactionCommandHandler : ICommandHandler<RemoveReactionCommand, ReactionSummaryResponse>
{
    private readonly IReactionTargetReader _targets;
    private readonly IReactionRepository _reactions;
    private readonly IEngagementQueries _queries;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public RemoveReactionCommandHandler(
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
        RemoveReactionCommand command, CancellationToken cancellationToken)
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
        if (existing is not null)
        {
            _reactions.Remove(existing);
            _outbox.Enqueue(new ReactionRemovedIntegrationEvent(
                Ulid.NewUlid(), userId, ReactionTargets.ToWire(command.TargetType), command.TargetId,
                ReactionTypes.Like, target.CategoryId, target.ThreadId, _clock.GetUtcNow()));
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result.Success(
            await _queries.GetSummaryAsync(command.TargetType, command.TargetId, userId, cancellationToken));
    }
}
