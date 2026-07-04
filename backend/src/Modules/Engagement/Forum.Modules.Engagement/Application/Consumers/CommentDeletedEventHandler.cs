using Forum.Common.Messaging;
using Forum.Modules.Content.Contracts.IntegrationEvents;
using Forum.Modules.Engagement.Application.Abstractions;
using Forum.Modules.Engagement.Domain.Reactions;

using Microsoft.Extensions.Logging;

namespace Forum.Modules.Engagement.Application.Consumers;

/// <summary>
/// Cascade for comment deletion: removes the comment's reactions; the counter trigger keeps
/// <c>reaction_counts</c> in step. Wired for the Phase 6 RabbitMQ relay; until then it can be invoked
/// in-process. Idempotent: a duplicate delivery deletes nothing.
/// </summary>
internal sealed class CommentDeletedEventHandler : IIntegrationEventHandler<CommentDeletedIntegrationEvent>
{
    private readonly IReactionRepository _reactions;
    private readonly ILogger<CommentDeletedEventHandler> _logger;

    public CommentDeletedEventHandler(IReactionRepository reactions, ILogger<CommentDeletedEventHandler> logger)
    {
        _reactions = reactions;
        _logger = logger;
    }

    public async Task HandleAsync(CommentDeletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var removed = await _reactions.DeleteAllForTargetAsync(
            ReactionTargetType.Comment, integrationEvent.CommentId, cancellationToken);

        if (removed > 0 && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Removed {Count} reaction(s) from deleted comment {CommentId} (event {EventId}).",
                removed, integrationEvent.CommentId, integrationEvent.EventId);
        }
    }
}
