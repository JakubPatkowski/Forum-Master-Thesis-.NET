using Forum.Modules.Content.Domain.Categories;
using Forum.Modules.Content.Domain.Categories.Events;

using Shouldly;

using Xunit;

namespace Forum.Modules.Content.Tests.Unit;

public sealed class CategoryTests
{
    [Fact]
    public void Create_sets_ownership_and_raises_the_event()
    {
        var owner = Ulid.NewUlid();

        var category = Category.Create("fly-fishing", "Fly fishing", "All about flies.", Visibility.Public, owner);

        category.Slug.ShouldBe("fly-fishing");
        category.OwnerId.ShouldBe(owner);
        category.Visibility.ShouldBe(Visibility.Public);
        category.IsDeleted.ShouldBeFalse();

        var raised = category.DomainEvents.OfType<CategoryCreatedDomainEvent>().ShouldHaveSingleItem();
        raised.CategoryId.ShouldBe(category.Id);
        raised.Slug.ShouldBe("fly-fishing");
    }

    [Fact]
    public void UpdateDetails_and_visibility_replace_the_fields()
    {
        var category = Category.Create("gear", "Gear", string.Empty, Visibility.Public, Ulid.NewUlid());

        category.UpdateDetails(" Tackle ", " Rods and reels ");
        category.ChangeVisibility(Visibility.Private);

        category.Name.ShouldBe("Tackle");
        category.Description.ShouldBe("Rods and reels");
        category.Visibility.ShouldBe(Visibility.Private);
    }

    [Fact]
    public void Delete_flags_the_category()
    {
        var category = Category.Create("gear", "Gear", string.Empty, Visibility.Public, Ulid.NewUlid());
        var moderator = Ulid.NewUlid();
        var now = DateTimeOffset.UtcNow;

        var result = category.Delete(moderator, now);

        result.IsSuccess.ShouldBeTrue();
        category.IsDeleted.ShouldBeTrue();
        category.DeletedBy.ShouldBe(moderator);
        category.DeletedOnUtc.ShouldBe(now);
    }

    [Fact]
    public void Deleting_twice_fails()
    {
        var category = Category.Create("gear", "Gear", string.Empty, Visibility.Public, Ulid.NewUlid());
        category.Delete(Ulid.NewUlid(), DateTimeOffset.UtcNow);

        var result = category.Delete(Ulid.NewUlid(), DateTimeOffset.UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(CategoryErrors.AlreadyDeleted);
    }
}
