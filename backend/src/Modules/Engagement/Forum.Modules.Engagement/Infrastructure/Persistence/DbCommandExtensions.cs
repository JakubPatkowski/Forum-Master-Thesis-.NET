using System.Data;
using System.Data.Common;

using Npgsql;

using NpgsqlTypes;

namespace Forum.Modules.Engagement.Infrastructure.Persistence;

/// <summary>Small helper for the raw-ADO read paths (counter/view queries) that bypass the EF model.</summary>
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

    /// <summary>Adds a <c>text[]</c> parameter for <c>= ANY(@ids)</c> batch filters (Npgsql-specific type).</summary>
    public static void AddTextArrayParameter(this DbCommand command, string name, string[] values)
    {
        var parameter = (NpgsqlParameter)command.CreateParameter();
        parameter.ParameterName = name;
        parameter.NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text;
        parameter.Value = values;
        command.Parameters.Add(parameter);
    }
}
