using System.Reflection;

namespace Forum.Modules.Social;

public sealed class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
