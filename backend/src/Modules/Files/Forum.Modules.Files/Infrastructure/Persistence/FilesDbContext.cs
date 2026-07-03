using Forum.Infrastructure.Messaging;
using Forum.Infrastructure.Messaging.Outbox;
using Forum.Infrastructure.Persistence;
using Forum.Modules.Files.Domain.Files;
using Forum.Modules.Files.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;

namespace Forum.Modules.Files.Infrastructure.Persistence;

/// <summary>
/// The Files module's unit of work. Owns the <c>forum_files</c> schema (files, file_attachments, outbox).
/// Targets are logical ULID references into other modules' schemas — no cross-schema FK exists here.
/// </summary>
internal sealed class FilesDbContext : ForumDbContext
{
    public const string Schema = "forum_files";

    public FilesDbContext(DbContextOptions<FilesDbContext> options, IDomainEventDispatcher dispatcher)
        : base(options, dispatcher)
    {
    }

    public DbSet<StoredFile> Files => Set<StoredFile>();

    public DbSet<FileAttachment> FileAttachments => Set<FileAttachment>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfiguration(new StoredFileConfiguration());
        modelBuilder.ApplyConfiguration(new FileAttachmentConfiguration());
        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());

        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        configurationBuilder.Properties<Ulid>()
            .HaveConversion<UlidToStringConverter>()
            .HaveMaxLength(26)
            .AreUnicode(false);
}
