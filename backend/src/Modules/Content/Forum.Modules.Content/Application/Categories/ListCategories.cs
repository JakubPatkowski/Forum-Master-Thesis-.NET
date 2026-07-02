using Forum.Common.Cqrs;
using Forum.Modules.Content.Application.Abstractions;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Categories;

/// <summary>Lists all live categories. Deliberately unpaged — categories are few by design.</summary>
internal sealed record ListCategoriesQuery : IQuery<IReadOnlyList<CategoryResponse>>;

internal sealed class ListCategoriesQueryHandler : IQueryHandler<ListCategoriesQuery, IReadOnlyList<CategoryResponse>>
{
    private readonly IContentQueries _queries;

    public ListCategoriesQueryHandler(IContentQueries queries) => _queries = queries;

    public async Task<Result<IReadOnlyList<CategoryResponse>>> Handle(
        ListCategoriesQuery query, CancellationToken cancellationToken) =>
        Result.Success(await _queries.ListCategoriesAsync(cancellationToken));
}
