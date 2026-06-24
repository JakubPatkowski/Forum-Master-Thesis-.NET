using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Forum.Infrastructure.Startup;

public static class StartupTaskRunner
{
    /// <summary>Runs ordered startup tasks, then serves. In k8s, migrations run as a Job — gate by config.</summary>
    public static async Task RunWithStartupTasksAsync(this WebApplication app)
    {
        using (var scope = app.Services.CreateScope())
        {
            var tasks = scope.ServiceProvider.GetServices<IStartupTask>().OrderBy(static task => task.Order);
            foreach (var task in tasks)
            {
                await task.ExecuteAsync(app.Lifetime.ApplicationStopping);
            }
        }

        await app.RunAsync();
    }
}
