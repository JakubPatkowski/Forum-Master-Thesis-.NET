using Forum.Modules.Social.Domain.Conversations;

using Shouldly;

using Xunit;

namespace Forum.Modules.Social.Tests.Unit;

/// <summary>Message tombstone semantics — the Comment.Delete mirror: row survives, body masks, edits lock out.</summary>
public sealed class MessageTests
{
    private readonly Ulid _sender = Ulid.NewUlid();

    private Message CreateMessage() => Message.Create(Ulid.NewUlid(), _sender, "hello there");

    [Fact]
    public void Editing_stamps_the_edited_marker()
    {
        var message = CreateMessage();
        var editedOn = DateTimeOffset.UtcNow;

        message.Edit("hello again", editedOn).IsSuccess.ShouldBeTrue();

        message.Body.ShouldBe("hello again");
        message.EditedOnUtc.ShouldBe(editedOn);
    }

    [Fact]
    public void Deleting_tombstones_the_body_and_keeps_the_row_facts()
    {
        var message = CreateMessage();
        var deletedBy = Ulid.NewUlid();

        message.Delete(deletedBy, DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();

        message.IsDeleted.ShouldBeTrue();
        message.Body.ShouldBe(Message.DeletedBody);
        message.DeletedBy.ShouldBe(deletedBy);
        message.ConversationId.ShouldNotBe(default);
    }

    [Fact]
    public void A_tombstone_can_be_neither_edited_nor_deleted_again()
    {
        var message = CreateMessage();
        message.Delete(_sender, DateTimeOffset.UtcNow);

        message.Edit("resurrect", DateTimeOffset.UtcNow).Error.ShouldBe(MessageErrors.Deleted);
        message.Delete(_sender, DateTimeOffset.UtcNow).Error.ShouldBe(MessageErrors.Deleted);
    }
}

/// <summary>The canonical direct-pair key is order-independent — the race-closing invariant.</summary>
public sealed class ConversationTests
{
    [Fact]
    public void The_direct_key_is_identical_regardless_of_argument_order()
    {
        var a = Ulid.NewUlid();
        var b = Ulid.NewUlid();

        Conversation.BuildDirectKey(a, b).ShouldBe(Conversation.BuildDirectKey(b, a));
    }

    [Fact]
    public void A_group_conversation_reuses_the_groups_id()
    {
        var groupId = Ulid.NewUlid();
        var conversation = Conversation.CreateForGroup(groupId);

        conversation.Id.ShouldBe(groupId);
        conversation.Type.ShouldBe(ConversationType.Group);
        conversation.DirectKey.ShouldBeNull();
    }
}
