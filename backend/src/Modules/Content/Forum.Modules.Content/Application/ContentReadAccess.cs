using Forum.Common.Security;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Application.Categories;
using Forum.Modules.Content.Application.Threads;

namespace Forum.Modules.Content.Application;

/// <summary>The facts the read gate needs about one category: who owns it and whether it is private.</summary>
internal sealed record CategoryReadInfo(Ulid Id, Ulid OwnerId, bool IsPrivate);

/// <summary>A thread detail row plus its category's read-gate facts, fetched in one round-trip.</summary>
internal sealed record ThreadDetailRead(ThreadDetailResponse Detail, CategoryReadInfo Category);

/// <summary>
/// The Content READ gate (A-12): a private category's threads and comments are visible only to its owner and to
/// readers holding <c>moderate</c> at that category scope — the same owner-or-moderator rule every Content write
/// applies (global moderators/admins hold <c>moderate</c> everywhere via their role template; members of a private
/// category hold it through a category-scoped ACL entry). Everyone else gets a 404 (existence is not revealed) and
/// list reads (search, profile activity) silently omit the rows. Public content stays a single SQL filter — an
/// anonymous read never resolves a permission.
/// </summary>
internal sealed class ContentReadAccess
{
    private readonly ICurrentUser _currentUser;
    private readonly IContentQueries _queries;

    public ContentReadAccess(ICurrentUser currentUser, IContentQueries queries)
    {
        _currentUser = currentUser;
        _queries = queries;
    }

    /// <summary>The read-gate facts of a category read model (its <c>visibility</c> is the lower-cased enum name).</summary>
    public static CategoryReadInfo ReadInfo(CategoryResponse category) =>
        new(category.Id, category.OwnerId,
            string.Equals(category.Visibility, "private", StringComparison.OrdinalIgnoreCase));

    public async Task<bool> CanReadAsync(CategoryReadInfo category, CancellationToken cancellationToken) =>
        !category.IsPrivate
        || (_currentUser.IsAuthenticated
            && await _currentUser.IsOwnerOrModeratorAsync(category.OwnerId, category.Id, cancellationToken));

    /// <summary>
    /// The private categories the current reader may open — empty for anonymous readers (no query at all), so list
    /// reads reduce to "public categories only" on the hot path.
    /// </summary>
    public async Task<IReadOnlyCollection<Ulid>> ReadablePrivateCategoryIdsAsync(CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated)
        {
            return [];
        }

        var privateCategories = await _queries.ListPrivateCategoriesAsync(cancellationToken);
        var readable = new List<Ulid>(privateCategories.Count);
        foreach (var category in privateCategories)
        {
            if (await _currentUser.IsOwnerOrModeratorAsync(category.OwnerId, category.Id, cancellationToken))
            {
                readable.Add(category.Id);
            }
        }

        return readable;
    }
}
