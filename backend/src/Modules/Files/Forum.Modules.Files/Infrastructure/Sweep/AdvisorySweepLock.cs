using Forum.Modules.Files.Application.Abstractions;
using Forum.Modules.Files.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Forum.Modules.Files.Infrastructure.Sweep;

/// <summary>
/// Cross-replica sweep exclusion via a Postgres session advisory lock. The lock rides the module DbContext's
/// connection, which is explicitly opened on acquire and kept open until release (a session lock dies with its
/// connection, so a crashed replica frees it automatically).
/// </summary>
internal sealed class AdvisorySweepLock : ISweepLock
{
    /// <summary>Arbitrary but stable key identifying "the forum_files orphan sweep" across all replicas.</summary>
    private const long LockKey = 0x464C5357_46494C45; // "FLSW" "FILE"

    private readonly FilesDbContext _db;
    private bool _held;

    public AdvisorySweepLock(FilesDbContext db) => _db = db;

    public async Task<bool> TryAcquireAsync(CancellationToken cancellationToken)
    {
        var connection = _db.Database.GetDbConnection();
        await _db.Database.OpenConnectionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@key)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@key";
        parameter.Value = LockKey;
        command.Parameters.Add(parameter);

        _held = await command.ExecuteScalarAsync(cancellationToken) is true;
        if (!_held)
        {
            await _db.Database.CloseConnectionAsync();
        }

        return _held;
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken)
    {
        if (!_held)
        {
            return;
        }

        var connection = _db.Database.GetDbConnection();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT pg_advisory_unlock(@key)";
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@key";
            parameter.Value = LockKey;
            command.Parameters.Add(parameter);
            await command.ExecuteScalarAsync(cancellationToken);
        }

        _held = false;
        await _db.Database.CloseConnectionAsync();
    }
}
