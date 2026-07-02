using Forum.Common.Messaging;
using Forum.Modules.Identity.Contracts.IntegrationEvents;

using Microsoft.Extensions.Logging;

namespace Forum.Modules.Content.Application.Consumers;

/// <summary>
/// Reacts to a user being blocked. The consumer is wired now so the Phase 6 RabbitMQ relay has a target;
/// the actual content-side reaction (hiding/disabling the user's content) lands with that phase.
/// </summary>
internal sealed class UserBlockedEventHandler : IIntegrationEventHandler<UserBlockedIntegrationEvent>
{
    private readonly ILogger<UserBlockedEventHandler> _logger;

    public UserBlockedEventHandler(ILogger<UserBlockedEventHandler> logger) => _logger = logger;

    public Task HandleAsync(UserBlockedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Content module observed UserBlocked for {UserId} (event {EventId}).",
                integrationEvent.UserId,
                integrationEvent.EventId);
        }

        return Task.CompletedTask;
    }
}
