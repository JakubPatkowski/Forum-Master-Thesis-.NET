namespace Forum.Modules.Files.Domain.Files;

/// <summary>Lifecycle of an upload: a row is born pending at initiate and flips to committed after verification.</summary>
internal enum FileStatus
{
    Pending,
    Committed,
}
