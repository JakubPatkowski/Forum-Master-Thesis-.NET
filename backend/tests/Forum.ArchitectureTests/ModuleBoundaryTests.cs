using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace Forum.ArchitectureTests;

/// <summary>
/// Enforces module-first boundaries: modules talk only via *.Contracts, and within a module
/// the Domain folder stays free of Infrastructure/Presentation and frameworks.
/// </summary>
public class ModuleBoundaryTests
{
    private static readonly System.Reflection.Assembly Identity = Forum.Modules.Identity.AssemblyReference.Assembly;
    private static readonly System.Reflection.Assembly Content = Forum.Modules.Content.AssemblyReference.Assembly;
    private static readonly System.Reflection.Assembly Files = Forum.Modules.Files.AssemblyReference.Assembly;

    [Fact]
    public void Modules_communicate_only_through_contracts()
    {
        // Contracts/ is the sanctioned cross-module surface; every other layer of a module is off-limits
        // to the outside (they are `internal` too — this guards against the rule ever being relaxed).
        Types.InAssembly(Identity)
            .ShouldNot().HaveDependencyOnAny(
                "Forum.Modules.Content.Domain",
                "Forum.Modules.Content.Application",
                "Forum.Modules.Content.Infrastructure",
                "Forum.Modules.Content.Presentation")
            .GetResult().IsSuccessful.ShouldBeTrue("Identity may use Content only via Forum.Modules.Content.Contracts.");

        Types.InAssembly(Content)
            .ShouldNot().HaveDependencyOnAny(
                "Forum.Modules.Identity.Domain",
                "Forum.Modules.Identity.Application",
                "Forum.Modules.Identity.Infrastructure",
                "Forum.Modules.Identity.Presentation")
            .GetResult().IsSuccessful.ShouldBeTrue("Content may use Identity only via Forum.Modules.Identity.Contracts.");

        Types.InAssembly(Files)
            .ShouldNot().HaveDependencyOnAny(
                "Forum.Modules.Identity.Domain",
                "Forum.Modules.Identity.Application",
                "Forum.Modules.Identity.Infrastructure",
                "Forum.Modules.Identity.Presentation",
                "Forum.Modules.Content.Domain",
                "Forum.Modules.Content.Application",
                "Forum.Modules.Content.Infrastructure",
                "Forum.Modules.Content.Presentation")
            .GetResult().IsSuccessful.ShouldBeTrue("Files may use Identity/Content only via their Contracts.");

        // The dependency direction is one-way: upstream modules never reach into Files (not even its Contracts —
        // reacting to Files happens via integration events, keeping Identity ← Content ← Files acyclic).
        foreach (var upstream in new[] { Identity, Content })
        {
            Types.InAssembly(upstream)
                .ShouldNot().HaveDependencyOnAny("Forum.Modules.Files")
                .GetResult().IsSuccessful.ShouldBeTrue("Upstream modules must not depend on Files.");
        }
    }

    [Fact]
    public void Module_domain_stays_pure()
    {
        Types.InAssembly(Identity)
            .That().ResideInNamespace("Forum.Modules.Identity.Domain")
            .ShouldNot().HaveDependencyOnAny(
                "Forum.Modules.Identity.Infrastructure",
                "Forum.Modules.Identity.Presentation",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult().IsSuccessful.ShouldBeTrue("Module Domain must stay free of adapters and frameworks.");

        Types.InAssembly(Content)
            .That().ResideInNamespace("Forum.Modules.Content.Domain")
            .ShouldNot().HaveDependencyOnAny(
                "Forum.Modules.Content.Infrastructure",
                "Forum.Modules.Content.Presentation",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult().IsSuccessful.ShouldBeTrue("Module Domain must stay free of adapters and frameworks.");

        Types.InAssembly(Files)
            .That().ResideInNamespace("Forum.Modules.Files.Domain")
            .ShouldNot().HaveDependencyOnAny(
                "Forum.Modules.Files.Infrastructure",
                "Forum.Modules.Files.Presentation",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore")
            .GetResult().IsSuccessful.ShouldBeTrue("Module Domain must stay free of adapters and frameworks.");
    }
}
