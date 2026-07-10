using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using Shouldly;

using Xunit;

namespace Forum.IntegrationTests;

/// <summary>
/// G19: the development signing key is public in source, so a Production host without a real
/// <c>Jwt:SigningKey</c> must refuse to boot — silently signing forgeable tokens is the worst failure mode.
/// No containers needed: the throw happens during service registration, before anything is dialed.
/// </summary>
public sealed class ProductionJwtFailFastTests
{
    [Fact]
    public void Production_refuses_to_boot_without_a_signing_key()
    {
        using var factory = new ProductionFactory();

        var exception = Should.Throw<Exception>(() => factory.CreateClient());

        Flatten(exception).ShouldContain(static e =>
            e is InvalidOperationException && e.Message.Contains("Jwt:SigningKey"));
    }

    private static IEnumerable<Exception> Flatten(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private sealed class ProductionFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Production");
    }
}
