using System.Data;
using System.Data.Common;

namespace Forum.Modules.Identity.Infrastructure.Persistence;

/// <summary>Small helper for the raw-ADO read paths (keyset list, role lookup) that bypass the EF model.</summary>
internal static class DbCommandExtensions
{
    public static void AddParameter(this DbCommand command, string name, object value, DbType? dbType = null)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;

        // Postgres cannot infer the type of a NULL/untyped parameter in "@p IS NULL OR col > @p"
        // (SqlState 42P08); pin it explicitly so the prepared statement resolves.
        if (dbType is { } type)
        {
            parameter.DbType = type;
        }

        command.Parameters.Add(parameter);
    }
}
