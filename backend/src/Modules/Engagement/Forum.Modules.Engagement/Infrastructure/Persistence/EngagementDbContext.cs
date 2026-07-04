using Forum.Infrastructure.Messaging;
using Forum.Infrastructure.Messaging.Outbox;
using Forum.Infrastructure.Persistence;
using Forum.Modules.Engagement.Domain.Reactions;
using Forum.Modules.Engagement.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;

namespace Forum.Modules.Engagement.Infrastructure.Persistence;

/// <summary>
/// The Engagement module's unit of work. Owns the <c>forum_engagement</c> schema (reactions, outbox).
/// The <c>reaction_counts</c> table (trigger-maintained) and the <c>user_stats_v</c> view ship in a raw-SQL
/// migration and stay outside this model: EF never writes either. Targets are logical ULID references into
/// <c>forum_content</c> — no cross-schema FK exists here.
/// </summary>
internal sealed class EngagementDbContext : ForumDbContext
{
    public const string Schema = "forum_engagement";

    public EngagementDbContext(DbContextOptions<EngagementDbContext> options, IDomainEventDispatcher dispatcher)
        : base(options, dispatcher)
    {
    }

    public DbSet<Reaction> Reactions => Set<Reaction>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfiguration(new ReactionConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());

        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<Ulid>()
            .HaveConversion<UlidToStringConverter>()
            .HaveMaxLength(26)
            .AreUnicode(false);
}
