using Forum.Common.Cqrs;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Domain.Categories;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Categories;

internal sealed record GetCategoryQuery(string Slug) : IQuery<CategoryResponse>;

/// <summary>
/// One category by slug. A private category the reader may not open is reported as missing (404) — existence is
/// not revealed, exactly like its threads and comments under the A-12 read gate.
/// </summary>
internal sealed class GetCategoryQueryHandler : IQueryHandler<GetCategoryQuery, CategoryResponse>
{
    private readonly IContentQueries _queries;
    private readonly ContentReadAccess _readAccess;

    public GetCategoryQueryHandler(IContentQueries queries, ContentReadAccess readAccess)
    {
        _queries = queries;
        _readAccess = readAccess;
    }

    public async Task<Result<CategoryResponse>> Handle(GetCategoryQuery query, CancellationToken cancellationToken)
    {
        var category = await _queries.GetCategoryBySlugAsync(query.Slug, cancellationToken);
        if (category is null
            || !await _readAccess.CanReadAsync(ContentReadAccess.ReadInfo(category), cancellationToken))
        {
            return Result.Failure<CategoryResponse>(CategoryErrors.NotFound);
        }

        return Result.Success(category);
    }
}
