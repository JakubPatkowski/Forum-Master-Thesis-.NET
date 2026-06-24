namespace Forum.Infrastructure.Startup;

/// <summary>Ordered boot work (migrations, SQL views/functions, seeding).</summary>
public interface IStartupTask
{
    int Order { get; }

    Task ExecuteAsync(CancellationToken cancellationToken);
}
