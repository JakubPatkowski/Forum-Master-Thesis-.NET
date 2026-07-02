using Forum.Modules.Content.Domain.Threads;

namespace Forum.Modules.Content.Application.Abstractions;

internal interface ITagRepository
{
    /// <summary>Existing tags matching any of the (already normalized) slugs.</summary>
    Task<IReadOnlyList<Tag>> GetBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken);

    void Add(Tag tag);
}
