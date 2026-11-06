using Forum.Modules.Content.Domain.Threads;

namespace Forum.Modules.Content.Application.Abstractions;

internal interface ITagRepository
{
    /// <summary>Existing tags matching any of the (already normalized) slugs.</summary>
    Task<IReadOnlyList<Tag>> GetBySlugsAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken);

    /// <summary>
    /// Race-safe get-or-create: every slug comes back as a tag, whether it existed, was created by this call or was
    /// created by a concurrent request a moment earlier. Two requests racing to create the same new slug both succeed
    /// (found live by the k6 harness: the former lookup-then-add raised a unique violation → 500 for the loser).
    /// </summary>
    Task<IReadOnlyList<Tag>> GetOrCreateAsync(IReadOnlyCollection<string> slugs, CancellationToken cancellationToken);
}
