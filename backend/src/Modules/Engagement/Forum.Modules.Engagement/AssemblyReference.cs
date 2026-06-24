using System.Reflection;

namespace Forum.Modules.Engagement;

public sealed class AssemblyReference
{
    public static readonly Assembly Assembly = typeof(AssemblyReference).Assembly;
}
