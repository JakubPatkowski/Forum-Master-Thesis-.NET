using Forum.Common.Messaging;
using Forum.Modules.Content.Contracts.IntegrationEvents;
using Forum.Modules.Engagement.Application.Abstractions;
using Forum.Modules.Engagement.Domain.Reactions;

using Microsoft.Extensions.Logging;

namespace Forum.Modules.Engagement.Application.Consumers;

/// <summary>
/// Keeps the logical reaction → thread link consistent (no cross-schema FK): when Content soft-deletes a thread,
/// its reactions are removed and the counter trigger folds <c>reaction_counts</c> down with them. Wired for the
/// Phase 6 RabbitMQ relay; until then it can be invoked in-process. Idempotent: a duplicate delivery deletes
/// nothing.
/// </summary>
internal sealed class ThreadDeletedEventHandler : IIntegrationEventHandler<ThreadDeletedIntegrationEvent>
{
    private readonly IReactionRepository _reactions;
    private readonly ILogger<ThreadDeletedEventHandler> _logger;

    public ThreadDeletedEventHandler(IReactionRepository reactions, ILogger<ThreadDeletedEventHandler> logger)
    {
        _reactions = reactions;
        _logger = logger;
    }

    public async Task HandleAsync(ThreadDeletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var removed = await _reactions.DeleteAllForTargetAsync(
            ReactionTargetType.Thread, integrationEvent.ThreadId, cancellationToken);

        if (removed > 0 && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Removed {Count} reaction(s) from deleted thread {ThreadId} (event {EventId}).",
                removed, integrationEvent.ThreadId, integrationEvent.EventId);
        }
    }
}
