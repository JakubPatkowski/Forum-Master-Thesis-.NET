namespace Forum.Modules.Engagement.Domain.Reactions;

/// <summary>What a reaction points at. Stored as text (Content/Files precedent), not a PG enum.</summary>
internal enum ReactionTargetType
{
    Thread,
    Comment,
}
