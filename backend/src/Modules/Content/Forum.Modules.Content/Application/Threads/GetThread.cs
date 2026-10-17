using Forum.Common.Cqrs;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Domain.Threads;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Threads;

internal sealed record GetThreadQuery(Ulid ThreadId) : IQuery<ThreadDetailResponse>;

internal sealed class GetThreadQueryHandler : IQueryHandler<GetThreadQuery, ThreadDetailResponse>
{
    private readonly IContentQueries _queries;
    private readonly ContentReadAccess _readAccess;

    public GetThreadQueryHandler(IContentQueries queries, ContentReadAccess readAccess)
    {
        _queries = queries;
        _readAccess = readAccess;
    }

    public async Task<Result<ThreadDetailResponse>> Handle(GetThreadQuery query, CancellationToken cancellationToken)
    {
        // A thread in a private category the reader cannot open is a 404, never a 403 (A-12).
        var thread = await _queries.GetThreadAsync(query.ThreadId, cancellationToken);
        return thread is null || !await _readAccess.CanReadAsync(thread.Category, cancellationToken)
            ? Result.Failure<ThreadDetailResponse>(ThreadErrors.NotFound)
            : Result.Success(thread.Detail);
    }
}
