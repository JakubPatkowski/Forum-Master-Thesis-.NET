using Forum.Modules.Content.Domain.Categories.Events;
using Forum.SharedKernel.Domain;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Domain.Categories;

/// <summary>
/// A forum category: the container threads live in. Owned by its creator (subreddit-style); ownership plus the
/// SQL ACL (moderate at category scope) gate who may modify it. Soft-deleted, never physically removed.
/// </summary>
internal sealed class Category : AggregateRoot<Ulid>, IOwned, ISoftDeletable
{
    // EF materialization.
    private Category()
    {
    }

    private Category(Ulid id, string slug, string name, string description, Visibility visibility, Ulid ownerId)
        : base(id)
    {
        Slug = slug;
        Name = name;
        Description = description;
        Visibility = visibility;
        OwnerId = ownerId;
    }

    /// <summary>URL identifier, unique and lower-case (validated at the edge).</summary>
    public string Slug { get; private set; } = default!;

    public string Name { get; private set; } = default!;

    public string Description { get; private set; } = default!;

    public Visibility Visibility { get; private set; }

    public Ulid OwnerId { get; private set; }

    /// <summary>Logical reference to <c>forum_files.files</c> (no cross-schema FK); attached in Phase 3.</summary>
    public Ulid? IconFileId { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedOnUtc { get; private set; }

    public Ulid? DeletedBy { get; private set; }

    /// <summary>Creates a category and raises <see cref="CategoryCreatedDomainEvent"/>. Inputs are pre-validated at the edge.</summary>
    public static Category Create(string slug, string name, string description, Visibility visibility, Ulid ownerId)
    {
        var category = new Category(Ulid.NewUlid(), slug.Trim(), name.Trim(), description.Trim(), visibility, ownerId);
        category.Raise(new CategoryCreatedDomainEvent(category.Id, category.Slug, ownerId, DateTimeOffset.UtcNow));
        return category;
    }

    public void UpdateDetails(string name, string description)
    {
        Name = name.Trim();
        Description = description.Trim();
    }

    public void ChangeVisibility(Visibility visibility) => Visibility = visibility;

    /// <summary>Soft-deletes the category. Threads inside stay untouched (their feed hides with the category).</summary>
    public Result Delete(Ulid deletedBy, DateTimeOffset onUtc)
    {
        if (IsDeleted)
        {
            return Result.Failure(CategoryErrors.AlreadyDeleted);
        }

        MarkDeleted(onUtc, deletedBy);
        return Result.Success();
    }

    public void MarkDeleted(DateTimeOffset onUtc, Ulid? by)
    {
        IsDeleted = true;
        DeletedOnUtc = onUtc;
        DeletedBy = by;
    }
}
