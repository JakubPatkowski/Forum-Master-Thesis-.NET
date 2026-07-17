using Forum.Common.Messaging;
using Forum.Modules.Files.Application.Abstractions;
using Forum.Modules.Files.Domain.Files;
using Forum.Modules.Social.Contracts.IntegrationEvents;

using Microsoft.Extensions.Logging;

namespace Forum.Modules.Files.Application.Consumers;

/// <summary>
/// When Social soft-deletes a group, its icon attachment is detached and the file rides the orphan sweep out —
/// the group-icon counterpart of the thread/comment/message deletion consumers.
/// </summary>
internal sealed class GroupDeletedEventHandler : IIntegrationEventHandler<GroupDeletedIntegrationEvent>
{
    private readonly IStoredFileRepository _files;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<GroupDeletedEventHandler> _logger;

    public GroupDeletedEventHandler(
        IStoredFileRepository files, IUnitOfWork unitOfWork, ILogger<GroupDeletedEventHandler> logger)
    {
        _files = files;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task HandleAsync(GroupDeletedIntegrationEvent integrationEvent, CancellationToken cancellationToken)
    {
        var attached = await _files.GetAttachedToTargetAsync(
            FileTargetType.GroupIcon, integrationEvent.GroupId, cancellationToken);
        if (attached.Count == 0)
        {
            return;
        }

        foreach (var file in attached)
        {
            file.Detach(FileTargetType.GroupIcon, integrationEvent.GroupId);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Detached {Count} icon file(s) from deleted group {GroupId} (event {EventId}).",
                attached.Count, integrationEvent.GroupId, integrationEvent.EventId);
        }
    }
}
