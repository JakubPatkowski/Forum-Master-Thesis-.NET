namespace Forum.Modules.Files.Domain.Files;

/// <summary>
/// The kind of object a file attaches to. Together with the target ULID this is the logical "FK to the object
/// it's attached to" (no cross-schema DB FK; consistency comes from deletion-event consumers + the orphan sweep).
/// </summary>
internal enum FileTargetType
{
    Thread,
    Comment,
    CategoryIcon,
    ThreadIcon,
    Avatar,
    Dm,
}
