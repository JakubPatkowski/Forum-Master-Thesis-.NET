namespace Forum.Common.Paging;

/// <summary>Offset-based page (admin lists). Prefer <see cref="CursorPage{T}"/> for hot, deep lists.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, long TotalCount, int Page, int PageSize);

/// <summary>Keyset/cursor page: stable performance regardless of depth.</summary>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore);
