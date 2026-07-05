using Forum.Infrastructure.Messaging;
using Forum.Infrastructure.Messaging.Inbox;
using Forum.Infrastructure.Messaging.Outbox;
using Forum.Infrastructure.Persistence;
using Forum.Modules.Identity.Domain.Tokens;
using Forum.Modules.Identity.Domain.Users;
using Forum.Modules.Identity.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;

namespace Forum.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// The Identity module's unit of work. Owns the <c>forum_identity</c> schema (users, refresh tokens, outbox);
/// the <c>forum_authz</c> RBAC + ACL objects are added by a raw-SQL migration and accessed via SQL, not EF entities.
/// </summary>
internal sealed class IdentityDbContext : ForumDbContext
{
    public const string Schema = "forum_identity";

    public IdentityDbContext(DbContextOptions<IdentityDbContext> options, IDomainEventDispatcher dispatcher)
        : base(options, dispatcher)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.HasPostgresExtension("citext");

        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());

        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<Ulid>()
            .HaveConversion<UlidToStringConverter>()
            .HaveMaxLength(26)
            .AreUnicode(false);
}
