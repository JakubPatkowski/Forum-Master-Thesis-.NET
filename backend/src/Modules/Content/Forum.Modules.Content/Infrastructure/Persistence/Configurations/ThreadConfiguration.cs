using Forum.Modules.Content.Domain.Categories;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forum.Modules.Content.Infrastructure.Persistence.Configurations;

internal sealed class ThreadConfiguration : IEntityTypeConfiguration<Thread>
{
    public void Configure(EntityTypeBuilder<Thread> builder)
    {
        builder.ToTable("threads");
        builder.HasKey(thread => thread.Id);

        builder.Property(thread => thread.Title).IsRequired().HasMaxLength(200);
        builder.Property(thread => thread.Body).IsRequired();

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(thread => thread.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // The keyset feed index: pinned first, then newest, ULID id as the deterministic tiebreak.
        // Partial (live rows only) — exactly what the feed query scans.
        builder.HasIndex(thread => new { thread.CategoryId, thread.IsPinned, thread.CreatedOnUtc, thread.Id })
            .IsDescending(false, true, true, true)
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_threads_feed");

        // search_tsv (+ its GIN index and trigger) is added by the AddFtsAndViews raw-SQL migration and is
        // deliberately absent from the EF model, so EF never writes it and never tries to drop it.
    }
}
