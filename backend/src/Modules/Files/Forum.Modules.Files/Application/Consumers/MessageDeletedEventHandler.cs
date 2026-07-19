using Forum.Common.Messaging;
using Forum.Modules.Files.Application.Abstractions;
using Forum.Modules.Files.Domain.Files;
using Forum.Modules.Social.Contracts.IntegrationEvents;

using Microsoft.Extensions.Logging;

namespace Forum.Modules.Files.Application.Consumers;

/// <summary>
/// Keeps the logical file → message link consistent (no cross-schema FK): when Social tombstones a message, its
/// image attachments are detached; the now-unattached files are removed later by the orphan sweep — exactly the
/// ThreadDeleted/CommentDeleted pattern.
/// </summary>
internal sealed class MessageDeletedEventHandler : IIntegrationEventHandler<MessageDeletedIntegrationEvent>
{
    private readonly IStoredFileRepository _files;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<MessageDeletedEventHandler> _logger;

    public MessageDeletedEventHandler(
        IStoredFileRepository files, IUnitOfWork unitOfWork, ILogger<MessageDeletedEventHandler> logger)
    {
        _files = files;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(MessageDeletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var attached = await _files.GetAttachedToTargetAsync(
            FileTargetType.Message, integrationEvent.MessageId, cancellationToken);
        if (attached.Count == 0)
        {
            return;
        }

        foreach (var file in attached)
        {
            file.Detach(FileTargetType.Message, integrationEvent.MessageId);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Detached {Count} file(s) from deleted message {MessageId} (event {EventId}).",
                attached.Count, integrationEvent.MessageId, integrationEvent.EventId);
        }
    }
}
