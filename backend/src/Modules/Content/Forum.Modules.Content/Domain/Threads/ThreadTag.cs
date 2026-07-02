namespace Forum.Modules.Content.Domain.Threads;

/// <summary>Thread ↔ tag join row (composite key, not an aggregate).</summary>
internal sealed class ThreadTag
{
    private ThreadTag()
    {
    }

    public ThreadTag(Ulid threadId, Ulid tagId)
    {
        ThreadId = threadId;
        TagId = tagId;
    }

    public Ulid ThreadId { get; private set; }

    public Ulid TagId { get; private set; }
}
