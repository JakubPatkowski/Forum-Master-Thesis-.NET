using Forum.Modules.Social.Application.Abstractions;

using Microsoft.EntityFrameworkCore;

namespace Forum.Modules.Social.Infrastructure.Persistence;

/// <summary>
/// Read-only ADO peek into <c>forum_identity.users</c> (Engagement's ContentTargetReader precedent: a later
/// module may READ an earlier module's tables, never write them, never FK them). Non-active accounts are
/// socially invisible — a banned user reads as not-found for requests/invites/DMs.
/// </summary>
internal sealed class UserReader : IUserReader
{
    private readonly SocialDbContext _db;

    public UserReader(SocialDbContext db) => _db = db;

    public async Task<bool> IsActiveAsync(Ulid userId, CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM forum_identity.users WHERE id = @id AND status = 'active' LIMIT 1";
        command.AddParameter("@id", userId.ToString());

        var opened = false;
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
            opened = true;
        }

        try
        {
            return await command.ExecuteScalarAsync(cancellationToken) is not null;
        }
        finally
        {
            if (opened)
            {
                await connection.CloseAsync();
            }
        }
    }
}
