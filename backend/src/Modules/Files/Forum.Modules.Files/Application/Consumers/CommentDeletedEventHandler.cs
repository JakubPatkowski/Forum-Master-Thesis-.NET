using Forum.Common.Messaging;
using Forum.Modules.Content.Contracts.IntegrationEvents;
using Forum.Modules.Files.Application.Abstractions;
using Forum.Modules.Files.Domain.Files;

using Microsoft.Extensions.Logging;

namespace Forum.Modules.Files.Application.Consumers;

/// <summary>
/// Counterpart of <see cref="ThreadDeletedEventHandler"/> for comments: a deleted comment sheds its file
/// attachments; the orphan sweep collects whatever ends up unreferenced.
/// </summary>
internal sealed class CommentDeletedEventHandler : IIntegrationEventHandler<CommentDeletedIntegrationEvent>
{
    private readonly IStoredFileRepository _files;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CommentDeletedEventHandler> _logger;

    public CommentDeletedEventHandler(
        IStoredFileRepository files, IUnitOfWork unitOfWork, ILogger<CommentDeletedEventHandler> logger)
    {
        _files = files;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(CommentDeletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var attached = await _files.GetAttachedToTargetAsync(
            FileTargetType.Comment, integrationEvent.CommentId, cancellationToken);
        if (attached.Count == 0)
        {
            return;
        }

        foreach (var file in attached)
        {
            file.Detach(FileTargetType.Comment, integrationEvent.CommentId);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Detached {Count} file(s) from deleted comment {CommentId} (event {EventId}).",
                attached.Count, integrationEvent.CommentId, integrationEvent.EventId);
        }
    }
}
