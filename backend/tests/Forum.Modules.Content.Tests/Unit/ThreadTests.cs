using Forum.Modules.Content.Domain.Threads;
using Forum.Modules.Content.Domain.Threads.Events;

using Shouldly;

using Xunit;

namespace Forum.Modules.Content.Tests.Unit;

public sealed class ThreadTests
{
    [Fact]
    public void Create_sets_ownership_and_raises_the_event()
    {
        var categoryId = Ulid.NewUlid();
        var author = Ulid.NewUlid();

        var thread = Thread.Create(categoryId, author, "  A title  ", "body");

        thread.CategoryId.ShouldBe(categoryId);
        thread.OwnerId.ShouldBe(author);
        thread.Title.ShouldBe("A title");
        thread.IsPinned.ShouldBeFalse();

        var raised = thread.DomainEvents.OfType<ThreadCreatedDomainEvent>().ShouldHaveSingleItem();
        raised.ThreadId.ShouldBe(thread.Id);
        raised.CategoryId.ShouldBe(categoryId);
    }

    [Fact]
    public void Delete_flags_the_thread_and_raises_the_event()
    {
        var thread = Thread.Create(Ulid.NewUlid(), Ulid.NewUlid(), "title", "body");
        var moderator = Ulid.NewUlid();

        var result = thread.Delete(moderator, DateTimeOffset.UtcNow);

        result.IsSuccess.ShouldBeTrue();
        thread.IsDeleted.ShouldBeTrue();
        thread.DeletedBy.ShouldBe(moderator);
        thread.DomainEvents.OfType<ThreadDeletedDomainEvent>().ShouldHaveSingleItem().DeletedBy.ShouldBe(moderator);
    }

    [Fact]
    public void Deleting_twice_fails()
    {
        var thread = Thread.Create(Ulid.NewUlid(), Ulid.NewUlid(), "title", "body");
        thread.Delete(Ulid.NewUlid(), DateTimeOffset.UtcNow);

        var result = thread.Delete(Ulid.NewUlid(), DateTimeOffset.UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ThreadErrors.AlreadyDeleted);
    }

    [Fact]
    public void Pin_and_unpin_toggle_the_flag()
    {
        var thread = Thread.Create(Ulid.NewUlid(), Ulid.NewUlid(), "title", "body");

        thread.Pin();
        thread.IsPinned.ShouldBeTrue();

        thread.Unpin();
        thread.IsPinned.ShouldBeFalse();
    }

    [Fact]
    public void ChangeCategory_to_the_same_category_fails()
    {
        var categoryId = Ulid.NewUlid();
        var thread = Thread.Create(categoryId, Ulid.NewUlid(), "title", "body");

        var result = thread.ChangeCategory(categoryId);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(ThreadErrors.SameCategory);
    }

    [Fact]
    public void ChangeCategory_moves_the_thread()
    {
        var thread = Thread.Create(Ulid.NewUlid(), Ulid.NewUlid(), "title", "body");
        var target = Ulid.NewUlid();

        var result = thread.ChangeCategory(target);

        result.IsSuccess.ShouldBeTrue();
        thread.CategoryId.ShouldBe(target);
    }
}
