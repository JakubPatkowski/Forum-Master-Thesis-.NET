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

    [Fact]
    public void Modules_communicate_only_through_contracts()
    {
        Types.InAssembly(Identity).That().DoNotResideInNamespaceEndingWith("Contracts")
            .ShouldNot().HaveDependencyOn("Forum.Modules.Content")
            .GetResult().IsSuccessful.ShouldBeTrue("Identity may use Content only via Forum.Modules.Content.Contracts.");

        Types.InAssembly(Content).That().DoNotResideInNamespaceEndingWith("Contracts")
            .ShouldNot().HaveDependencyOn("Forum.Modules.Identity")
            .GetResult().IsSuccessful.ShouldBeTrue("Content may use Identity only via Forum.Modules.Identity.Contracts.");
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
    }
}
