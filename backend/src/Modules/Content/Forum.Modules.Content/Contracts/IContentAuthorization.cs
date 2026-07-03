using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Contracts;

/// <summary>A Content-owned object a file can be attached to.</summary>
public enum ContentAttachmentTarget
{
    Thread,
    Comment,
    CategoryIcon,
}

/// <summary>
/// Content's authorization surface for other modules. Files asks "may this user attach/detach a file on this
/// content object?" and Content answers with its own rules (existence → 404, owner-or-moderator at the category
/// scope → 403) — the rules stay inside Content, callers only consume the verdict. This keeps the dependency
/// direction acyclic: Files → Content (Files already consumes Content's deletion events), never Content → Files.
/// </summary>
public interface IContentAuthorization
{
    /// <summary>
    /// Success when <paramref name="userId"/> may modify the target's attachments; otherwise the same
    /// NotFound/Forbidden errors Content's own write handlers would return (404 → 403 order preserved).
    /// </summary>
    Task<Result> AuthorizeAttachmentAsync(
        ContentAttachmentTarget target, Ulid targetId, Ulid userId, CancellationToken cancellationToken);
}
