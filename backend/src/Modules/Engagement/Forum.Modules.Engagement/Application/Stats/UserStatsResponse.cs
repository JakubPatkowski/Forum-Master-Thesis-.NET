namespace Forum.Modules.Engagement.Application.Stats;

/// <summary>
/// Per-user public stats from <c>user_stats_v</c>: live thread/comment counts plus karma — the signed sum of
/// reaction values received on the user's live content.
/// </summary>
internal sealed record UserStatsResponse(
    Ulid UserId, string Username, string DisplayName, int ThreadCount, int CommentCount, int Karma);
