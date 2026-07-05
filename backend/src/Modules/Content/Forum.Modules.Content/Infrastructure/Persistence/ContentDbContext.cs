using Forum.Infrastructure.Messaging;
using Forum.Infrastructure.Messaging.Inbox;
using Forum.Infrastructure.Messaging.Outbox;
using Forum.Infrastructure.Persistence;
using Forum.Modules.Content.Domain.Categories;
using Forum.Modules.Content.Domain.Comments;
using Forum.Modules.Content.Domain.Threads;
using Forum.Modules.Content.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;

namespace Forum.Modules.Content.Infrastructure.Persistence;

/// <summary>
/// The Content module's unit of work. Owns the <c>forum_content</c> schema (categories, threads, comments,
/// tags, outbox). The FTS column/trigger and the read views ship in a raw-SQL migration and are outside this model.
/// </summary>
internal sealed class ContentDbContext : ForumDbContext
{
    public const string Schema = "forum_content";

    public ContentDbContext(DbContextOptions<ContentDbContext> options, IDomainEventDispatcher dispatcher)
        : base(options, dispatcher)
    {
    }

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Thread> Threads => Set<Thread>();

    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<Tag> Tags => Set<Tag>();

    public DbSet<ThreadTag> ThreadTags => Set<ThreadTag>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfiguration(new CategoryConfiguration());
        modelBuilder.ApplyConfiguration(new ThreadConfiguration());
        modelBuilder.ApplyConfiguration(new CommentConfiguration());
        modelBuilder.ApplyConfiguration(new TagConfiguration());
        modelBuilder.ApplyConfiguration(new ThreadTagConfiguration());
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
