namespace Forum.Infrastructure.Seeding;

/// <summary>
/// The named entity "streams" the deterministic id/timestamp generators are keyed by. Centralised here so every
/// module seeder derives cross-module references (e.g. a thread's owner, a reaction's target) from the SAME
/// convention without a project reference — module isolation is preserved, only the string keys are shared.
/// </summary>
public static class SeedStreams
{
    public const string User = "user";
    public const string Category = "category";
    public const string Tag = "tag";
    public const string Thread = "thread";
    public const string Comment = "comment";
    public const string Reaction = "reaction";

    // Phase 11 — Social. Group conversations deliberately have NO stream: a group chat reuses its group's id.
    public const string Friendship = "friendship";
    public const string Group = "group";
    public const string GroupInvite = "group-invite";
    public const string Conversation = "conversation";
    public const string Message = "message";
    public const string SocialNotification = "social-notification";
}
