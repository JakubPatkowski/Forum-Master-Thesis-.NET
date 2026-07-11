namespace Forum.Infrastructure.Seeding;

/// <summary>
/// A module's contribution to the offline seed. Resolved and run by <see cref="Startup.SeedRunner"/> in
/// <see cref="Order"/> sequence (Identity → Content → Engagement) so cross-module references (a thread's owner,
/// a reaction's target) point at rows that already exist. Mirrors <see cref="Startup.IStartupTask"/> but is
/// invoked ONLY by the <c>seed</c> CLI entrypoint — never on a normal boot.
/// </summary>
public interface IModuleSeeder
{
    /// <summary>Run order; lower runs first. Must respect the module dependency direction.</summary>
    int Order { get; }

    /// <summary>Populates this module's tables for the given profile (or resets them first when forced).</summary>
    Task SeedAsync(SeedConfig config, CancellationToken cancellationToken);
}
