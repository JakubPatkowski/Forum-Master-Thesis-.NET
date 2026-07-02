namespace Forum.Modules.Content.Application.Abstractions;

internal interface IThreadRepository
{
    /// <summary>Tracked load for writes; the soft-delete filter hides deleted threads.</summary>
    Task<Thread?> GetByIdAsync(Ulid id, CancellationToken cancellationToken);

    void Add(Thread thread);

    /// <summary>Stages thread↔tag join rows for the next save.</summary>
    void AttachTags(Ulid threadId, IReadOnlyCollection<Ulid> tagIds);
}
