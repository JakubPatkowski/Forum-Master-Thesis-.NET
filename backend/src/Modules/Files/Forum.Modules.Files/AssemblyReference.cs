using System.Reflection;

namespace Forum.Modules.Files;

public sealed class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
