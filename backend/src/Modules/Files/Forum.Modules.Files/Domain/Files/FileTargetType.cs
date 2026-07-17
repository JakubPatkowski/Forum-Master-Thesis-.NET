namespace Forum.Modules.Files.Domain.Files;

/// <summary>
/// The kind of object a file attaches to. Together with the target ULID this is the logical "FK to the object
/// it's attached to" (no cross-schema DB FK; consistency comes from deletion-event consumers + the orphan sweep).
/// <c>Message</c> repurposes Phase 3's never-materialized <c>Dm</c> placeholder (its 422 stub meant no 'dm' row
/// ever existed): one target type covers DM and group-chat images, because Social unified both into messages.
/// </summary>
internal enum FileTargetType
{
    Thread,
    Comment,
    CategoryIcon,
    ThreadIcon,
    Avatar,
    Message,
    GroupIcon,
}
