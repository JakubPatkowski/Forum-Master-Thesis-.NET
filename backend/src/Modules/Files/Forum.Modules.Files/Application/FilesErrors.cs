using Forum.SharedKernel.Results;

namespace Forum.Modules.Files.Application;

/// <summary>Cross-cutting errors of the Files application layer.</summary>
internal static class FilesErrors
{
    public static readonly Error AuthenticationRequired =
        Error.Unauthorized("auth.required", "Authentication required.");
}
