namespace Forum.Modules.Engagement.Application.Reactions;

/// <summary>Public reaction state of one target: the denormalized 'like' tally plus the viewer's own state.</summary>
internal sealed record ReactionSummaryResponse(Ulid TargetId, int Count, bool ViewerReacted);
