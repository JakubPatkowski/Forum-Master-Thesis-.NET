using Forum.Modules.Content.Domain.Threads.Events;
using Forum.SharedKernel.Domain;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Domain.Threads;

/// <summary>
/// A discussion thread inside a category. Body is markdown stored raw (sanitized at render, never here).
/// The <c>search_tsv</c> FTS column is created and maintained entirely by a database trigger and is deliberately
/// NOT part of this model — EF never reads or writes it.
/// </summary>
internal sealed class Thread : AggregateRoot<Ulid>, IOwned, ISoftDeletable
{
    // EF materialization.
    private Thread()
    {
    }

    private Thread(Ulid id, Ulid categoryId, Ulid ownerId, string title, string body)
        : base(id)
    {
        CategoryId = categoryId;
        OwnerId = ownerId;
        Title = title;
        Body = body;
    }

    public Ulid CategoryId { get; private set; }

    public Ulid OwnerId { get; private set; }

    public string Title { get; private set; } = default!;

    /// <summary>Markdown, stored raw.</summary>
    public string Body { get; private set; } = default!;

    public bool IsPinned { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset? DeletedOnUtc { get; private set; }

    public Ulid? DeletedBy { get; private set; }

    /// <summary>Creates a thread and raises <see cref="ThreadCreatedDomainEvent"/>. Inputs are pre-validated at the edge.</summary>
    public static Thread Create(Ulid categoryId, Ulid ownerId, string title, string body)
    {
        var thread = new Thread(Ulid.NewUlid(), categoryId, ownerId, title.Trim(), body);
        thread.Raise(new ThreadCreatedDomainEvent(thread.Id, categoryId, ownerId, thread.Title, DateTimeOffset.UtcNow));
        return thread;
    }

    /// <summary>Constructs a thread directly for the offline seeder: deterministic id + audit, no event raised.</summary>
    internal static Thread Seed(
        Ulid id, Ulid categoryId, Ulid ownerId, string title, string body, bool isPinned,
        DateTimeOffset createdOnUtc, bool isDeleted)
    {
        var thread = new Thread(id, categoryId, ownerId, title.Trim(), body) { IsPinned = isPinned };
        thread.SetCreated(createdOnUtc, ownerId);
        if (isDeleted)
        {
            thread.MarkDeleted(createdOnUtc, ownerId);
        }

        return thread;
    }

    public void Update(string title, string body)
    {
        Title = title.Trim();
        Body = body;
    }

    public void Pin() => IsPinned = true;

    public void Unpin() => IsPinned = false;

    public Result ChangeCategory(Ulid categoryId)
    {
        if (categoryId == CategoryId)
        {
            return Result.Failure(ThreadErrors.SameCategory);
        }

        CategoryId = categoryId;
        return Result.Success();
    }

    /// <summary>Soft-deletes the thread and raises <see cref="ThreadDeletedDomainEvent"/>. Comments stay in place.</summary>
    public Result Delete(Ulid deletedBy, DateTimeOffset onUtc)
    {
        if (IsDeleted)
        {
            return Result.Failure(ThreadErrors.AlreadyDeleted);
        }

        MarkDeleted(onUtc, deletedBy);
        Raise(new ThreadDeletedDomainEvent(Id, deletedBy, onUtc));
        return Result.Success();
    }

    public void MarkDeleted(DateTimeOffset onUtc, Ulid? by)
    {
        IsDeleted = true;
        DeletedOnUtc = onUtc;
        DeletedBy = by;
    }
}
