using Forum.Common.Cqrs;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Domain.Categories;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Categories;

internal sealed record GetCategoryQuery(string Slug) : IQuery<CategoryResponse>;

internal sealed class GetCategoryQueryHandler : IQueryHandler<GetCategoryQuery, CategoryResponse>
{
    private readonly IContentQueries _queries;

    public GetCategoryQueryHandler(IContentQueries queries) => _queries = queries;

    public async Task<Result<CategoryResponse>> Handle(GetCategoryQuery query, CancellationToken cancellationToken)
    {
        var category = await _queries.GetCategoryBySlugAsync(query.Slug, cancellationToken);
        return category is null
            ? Result.Failure<CategoryResponse>(CategoryErrors.NotFound)
            : Result.Success(category);
    }
}
