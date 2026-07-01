using System.Data.Common;

namespace Forum.Modules.Identity.Infrastructure.Persistence;

/// <summary>Small helper for the raw-ADO read paths (keyset list, role lookup) that bypass the EF model.</summary>
internal static class DbCommandExtensions
{
    public static void AddParameter(this DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
