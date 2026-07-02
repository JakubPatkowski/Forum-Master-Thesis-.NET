using Forum.Modules.Content.Domain.Comments;
using Forum.Modules.Content.Domain.Comments.Events;

using Shouldly;

using Xunit;

namespace Forum.Modules.Content.Tests.Unit;

public sealed class CommentTests
{
    [Fact]
    public void CreateRoot_uses_its_own_ulid_as_path_at_depth_zero()
    {
        var threadId = Ulid.NewUlid();
        var author = Ulid.NewUlid();

        var comment = Comment.CreateRoot(threadId, author, "hello");

        comment.Path.ShouldBe(comment.Id.ToString());
        comment.Depth.ShouldBe(0);
        comment.ParentId.ShouldBeNull();
        comment.ThreadId.ShouldBe(threadId);
        comment.OwnerId.ShouldBe(author);
        comment.DomainEvents.OfType<CommentCreatedDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void CreateReply_extends_the_parent_path_and_depth()
    {
        var parent = Comment.CreateRoot(Ulid.NewUlid(), Ulid.NewUlid(), "root");

        var result = Comment.CreateReply(parent, Ulid.NewUlid(), "reply");

        result.IsSuccess.ShouldBeTrue();
        var reply = result.Value;
        reply.Path.ShouldBe($"{parent.Path}.{reply.Id}");
        reply.Depth.ShouldBe(1);
        reply.ParentId.ShouldBe(parent.Id);
        reply.ThreadId.ShouldBe(parent.ThreadId);
    }

    [Fact]
    public void Replies_nest_to_the_depth_cap_and_no_further()
    {
        var comment = Comment.CreateRoot(Ulid.NewUlid(), Ulid.NewUlid(), "level 0");

        // Nest down to depth 5 (the cap) — every step succeeds.
        for (var depth = 1; depth <= Comment.MaxDepth; depth++)
        {
            var reply = Comment.CreateReply(comment, Ulid.NewUlid(), $"level {depth}");
            reply.IsSuccess.ShouldBeTrue();
            comment = reply.Value;
            comment.Depth.ShouldBe(depth);
        }

        // One more level exceeds the cap.
        var tooDeep = Comment.CreateReply(comment, Ulid.NewUlid(), "level 6");

        tooDeep.IsFailure.ShouldBeTrue();
        tooDeep.Error.ShouldBe(CommentErrors.MaxDepthExceeded);
    }

    [Fact]
    public void Delete_blanks_the_body_and_flags_the_row()
    {
        var comment = Comment.CreateRoot(Ulid.NewUlid(), Ulid.NewUlid(), "original body");
        var moderator = Ulid.NewUlid();
        var now = DateTimeOffset.UtcNow;

        var result = comment.Delete(moderator, now);

        result.IsSuccess.ShouldBeTrue();
        comment.IsDeleted.ShouldBeTrue();
        comment.Body.ShouldBe(Comment.DeletedBody);
        comment.DeletedBy.ShouldBe(moderator);
        comment.DeletedOnUtc.ShouldBe(now);
        comment.DomainEvents.OfType<CommentDeletedDomainEvent>().ShouldHaveSingleItem();
    }

    [Fact]
    public void Deleting_twice_fails()
    {
        var comment = Comment.CreateRoot(Ulid.NewUlid(), Ulid.NewUlid(), "body");
        comment.Delete(Ulid.NewUlid(), DateTimeOffset.UtcNow);

        var result = comment.Delete(Ulid.NewUlid(), DateTimeOffset.UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(CommentErrors.AlreadyDeleted);
    }

    [Fact]
    public void Deleting_a_parent_does_not_touch_the_reply_path()
    {
        var parent = Comment.CreateRoot(Ulid.NewUlid(), Ulid.NewUlid(), "root");
        var reply = Comment.CreateReply(parent, Ulid.NewUlid(), "child").Value;

        parent.Delete(Ulid.NewUlid(), DateTimeOffset.UtcNow);

        reply.IsDeleted.ShouldBeFalse();
        reply.Body.ShouldBe("child");
        reply.Path.ShouldStartWith(parent.Path);
    }
}
