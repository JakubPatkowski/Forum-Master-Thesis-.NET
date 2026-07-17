using Forum.Infrastructure.Messaging;
using Forum.SharedKernel.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Forum.Modules.Social.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by <c>dotnet ef migrations</c>. The connection string is never opened for scaffolding;
/// the no-op dispatcher satisfies the context's constructor (events are never dispatched at design time).
/// </summary>
internal sealed class SocialDbContextFactory : IDesignTimeDbContextFactory<SocialDbContext>
{
    public SocialDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SocialDbContext>()
            .UseNpgsql("Host=localhost;Database=forum_net;Username=forum;Password=forum")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new SocialDbContext(options, new NoOpDomainEventDispatcher());
    }

    private sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
