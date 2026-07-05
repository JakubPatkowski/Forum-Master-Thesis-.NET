using Forum.Modules.Engagement.Application.Abstractions;
using Forum.Modules.Engagement.Application.Consumers;
using Forum.Modules.Engagement.Domain.Reactions;

using Microsoft.Extensions.Logging.Abstractions;

using NSubstitute;

using Xunit;

using ContentEvents = Forum.Modules.Content.Contracts.IntegrationEvents;

namespace Forum.Modules.Engagement.Tests.Unit;

public sealed class DeletionConsumerTests
{
    private readonly IReactionRepository _reactions = Substitute.For<IReactionRepository>();

    [Fact]
    public async Task A_deleted_thread_cascades_to_its_reactions()
    {
        var threadId = Ulid.NewUlid();
        var handler = new ThreadDeletedEventHandler(
            _reactions, NullLogger<ThreadDeletedEventHandler>.Instance);

        await handler.HandleAsync(
            new ContentEvents.ThreadDeletedIntegrationEvent(
                Ulid.NewUlid(), threadId, Ulid.NewUlid(), DateTimeOffset.UtcNow),
            CancellationToken.None);

        await _reactions.Received(1).DeleteAllForTargetAsync(
            ReactionTargetType.Thread, threadId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_deleted_comment_cascades_to_its_reactions()
    {
        var commentId = Ulid.NewUlid();
        var handler = new CommentDeletedEventHandler(
            _reactions, NullLogger<CommentDeletedEventHandler>.Instance);

        await handler.HandleAsync(
            new ContentEvents.CommentDeletedIntegrationEvent(
                Ulid.NewUlid(), commentId, Ulid.NewUlid(), Ulid.NewUlid(), DateTimeOffset.UtcNow),
            CancellationToken.None);

        await _reactions.Received(1).DeleteAllForTargetAsync(
            ReactionTargetType.Comment, commentId, Arg.Any<CancellationToken>());
    }
}
