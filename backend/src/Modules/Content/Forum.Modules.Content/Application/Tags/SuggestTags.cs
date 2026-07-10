using Forum.Common.Cqrs;
using Forum.Modules.Content.Application.Abstractions;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Tags;

/// <summary>
/// Tag suggestions for autocomplete and the popular-tags panel: substring match on slug when a
/// query is given, otherwise the most-used tags. Ranked by live-thread usage either way.
/// </summary>
internal sealed record SuggestTagsQuery(string? Query, int Limit) : IQuery<IReadOnlyList<TagSuggestionResponse>>;

internal sealed class SuggestTagsQueryHandler : IQueryHandler<SuggestTagsQuery, IReadOnlyList<TagSuggestionResponse>>
{
    internal const int MaxLimit = 50;

    private readonly IContentQueries _queries;

    public SuggestTagsQueryHandler(IContentQueries queries) => _queries = queries;

    public async Task<Result<IReadOnlyList<TagSuggestionResponse>>> Handle(
        SuggestTagsQuery query, CancellationToken cancellationToken)
    {
        // Slugs are stored lower-case; normalizing here keeps the SQL a plain LIKE.
        var filter = query.Query?.Trim().ToLowerInvariant();
        var limit = Math.Clamp(query.Limit, 1, MaxLimit);
        return Result.Success(await _queries.SuggestTagsAsync(
            string.IsNullOrEmpty(filter) ? null : filter, limit, cancellationToken));
    }
}
