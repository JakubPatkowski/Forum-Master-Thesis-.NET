using Forum.Common.Cqrs;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Domain.Threads;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Threads;

internal sealed record GetThreadQuery(Ulid ThreadId) : IQuery<ThreadDetailResponse>;

internal sealed class GetThreadQueryHandler : IQueryHandler<GetThreadQuery, ThreadDetailResponse>
{
    private readonly IContentQueries _queries;

    public GetThreadQueryHandler(IContentQueries queries) => _queries = queries;

    public async Task<Result<ThreadDetailResponse>> Handle(GetThreadQuery query, CancellationToken cancellationToken)
    {
        var thread = await _queries.GetThreadAsync(query.ThreadId, cancellationToken);
        return thread is null
            ? Result.Failure<ThreadDetailResponse>(ThreadErrors.NotFound)
            : Result.Success(thread);
    }
}
