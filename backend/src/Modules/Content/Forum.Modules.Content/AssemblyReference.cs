using System.Reflection;

namespace Forum.Modules.Content;

public sealed class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
