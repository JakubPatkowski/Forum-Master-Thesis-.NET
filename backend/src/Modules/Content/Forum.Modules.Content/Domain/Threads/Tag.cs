using Forum.SharedKernel.Domain;

namespace Forum.Modules.Content.Domain.Threads;

/// <summary>A simple lookup label attached to threads. Not audited, never deleted — a slug is forever.</summary>
internal sealed class Tag : Entity<Ulid>
{
    // EF materialization.
    private Tag()
    {
    }

    private Tag(Ulid id, string slug, string name)
        : base(id)
    {
        Slug = slug;
        Name = name;
    }

    /// <summary>Unique lower-case identifier (validated at the edge).</summary>
    public string Slug { get; private set; } = default!;

    public string Name { get; private set; } = default!;

    public static Tag Create(string slug, string name) => new(Ulid.NewUlid(), slug, name);

    /// <summary>Constructs a tag directly for the offline seeder with a deterministic id.</summary>
    internal static Tag Seed(Ulid id, string slug, string name) => new(id, slug, name);
}
