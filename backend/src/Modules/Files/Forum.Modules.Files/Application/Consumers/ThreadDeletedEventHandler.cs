using Forum.Common.Messaging;
using Forum.Modules.Content.Contracts.IntegrationEvents;
using Forum.Modules.Files.Application.Abstractions;
using Forum.Modules.Files.Domain.Files;

using Microsoft.Extensions.Logging;

namespace Forum.Modules.Files.Application.Consumers;

/// <summary>
/// Keeps the logical file → thread link consistent (no cross-schema FK): when Content soft-deletes a thread,
/// its attachments are detached; the now-unattached files are removed later by the orphan sweep. Wired for the
/// Phase 6 RabbitMQ relay; until then it can be invoked in-process.
/// </summary>
internal sealed class ThreadDeletedEventHandler : IIntegrationEventHandler<ThreadDeletedIntegrationEvent>
{
    private readonly IStoredFileRepository _files;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<ThreadDeletedEventHandler> _logger;

    public ThreadDeletedEventHandler(
        IStoredFileRepository files, IUnitOfWork unitOfWork, ILogger<ThreadDeletedEventHandler> logger)
    {
        _files = files;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(ThreadDeletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var attached = await _files.GetAttachedToTargetAsync(
            FileTargetType.Thread, integrationEvent.ThreadId, cancellationToken);
        if (attached.Count == 0)
        {
            return;
        }

        foreach (var file in attached)
        {
            file.Detach(FileTargetType.Thread, integrationEvent.ThreadId);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Detached {Count} file(s) from deleted thread {ThreadId} (event {EventId}).",
                attached.Count, integrationEvent.ThreadId, integrationEvent.EventId);
        }
    }
}
