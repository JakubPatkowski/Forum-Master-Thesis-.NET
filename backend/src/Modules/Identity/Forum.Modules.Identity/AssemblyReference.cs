using System.Reflection;

namespace Forum.Modules.Identity;

public sealed class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
