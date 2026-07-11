namespace Forum.Infrastructure.Seeding;

/// <summary>
/// Immutable inputs for one seed run. Passed to every <see cref="IModuleSeeder"/> so counts (via
/// <see cref="Profile"/>) and the reset behaviour stay consistent across modules.
/// </summary>
/// <param name="Profile">Which dataset to produce.</param>
/// <param name="AllowTruncate">
/// When true (the CLI <c>--force</c> flag), each seeder TRUNCATEs its own tables before inserting, so a
/// non-empty database can be reset in place. When false the seeder aborts rather than touch existing data.
/// </param>
/// <param name="Verbose">Emit per-batch progress logging (useful for the slow Benchmark profile).</param>
public sealed record SeedConfig(SeedProfile Profile, bool AllowTruncate = false, bool Verbose = false);
