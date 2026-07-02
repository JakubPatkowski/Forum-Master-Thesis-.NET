using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application;

/// <summary>Cross-cutting errors of the Content application layer.</summary>
internal static class ContentErrors
{
    public static readonly Error AuthenticationRequired =
        Error.Unauthorized("auth.required", "Authentication required.");

    public static readonly Error InvalidCursor =
        Error.Validation("paging.invalid_cursor", "The paging cursor is malformed.");

    public static readonly Error EmptySearchQuery =
        Error.Validation("search.empty_query", "The search query must not be empty.");
}
