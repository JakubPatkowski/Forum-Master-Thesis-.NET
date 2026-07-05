namespace Forum.Modules.Content.Contracts;

/// <summary>
/// Content's visibility surface for other components. The WebSocket hub asks "who may see changes in this
/// category?" before every push and applies Content's own private-category rule (owner or <c>moderate</c> at the
/// category scope) — the rule's inputs stay inside Content, callers only consume the facts they need.
/// </summary>
public interface IContentVisibility
{
    /// <summary>The category's access facts, or null when it does not exist or is soft-deleted.</summary>
    Task<CategoryAccess?> GetCategoryAccessAsync(Ulid categoryId, CancellationToken cancellationToken);
}

/// <summary>What a caller needs to evaluate category visibility: the owner (always sees) and the private flag.</summary>
public sealed record CategoryAccess(Ulid CategoryId, Ulid OwnerId, bool IsPrivate);
