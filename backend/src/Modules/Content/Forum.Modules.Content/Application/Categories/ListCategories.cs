using Forum.Common.Cqrs;
using Forum.Modules.Content.Application.Abstractions;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Categories;

/// <summary>
/// Lists the live categories the current reader may open. Deliberately unpaged — categories are few by design.
/// Private categories are listed only for their owner and for readers holding <c>moderate</c> at that category
/// (the A-12 read gate, <see cref="ContentReadAccess"/>): an anonymous reader never learns that a private category
/// exists — the same posture Architecture B's <c>/communities</c> page has, and the one the SPA's home feed relies
/// on (it merges every listed category, so a listed-but-unreadable one would only ever answer 404).
/// </summary>
internal sealed record ListCategoriesQuery : IQuery<IReadOnlyList<CategoryResponse>>;

internal sealed class ListCategoriesQueryHandler : IQueryHandler<ListCategoriesQuery, IReadOnlyList<CategoryResponse>>
{
    private readonly IContentQueries _queries;
    private readonly ContentReadAccess _readAccess;

    public ListCategoriesQueryHandler(IContentQueries queries, ContentReadAccess readAccess)
    {
        _queries = queries;
        _readAccess = readAccess;
    }

    public async Task<Result<IReadOnlyList<CategoryResponse>>> Handle(
        ListCategoriesQuery query, CancellationToken cancellationToken)
    {
        var categories = await _queries.ListCategoriesAsync(cancellationToken);
        var visible = new List<CategoryResponse>(categories.Count);
        foreach (var category in categories)
        {
            // Public categories pass without a query; a private one costs the reader one owner-or-moderator check.
            if (await _readAccess.CanReadAsync(ContentReadAccess.ReadInfo(category), cancellationToken))
            {
                visible.Add(category);
            }
        }

        return Result.Success<IReadOnlyList<CategoryResponse>>(visible);
    }
}
