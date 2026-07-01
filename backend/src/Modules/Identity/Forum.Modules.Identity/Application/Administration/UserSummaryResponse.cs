namespace Forum.Modules.Identity.Application.Administration;

/// <summary>A row in the admin user list (read model).</summary>
internal sealed record UserSummaryResponse(
    Ulid Id, string Username, string DisplayName, string Email, string Status, DateTimeOffset CreatedOnUtc);
