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

        // Owner activity (profile page: a user's threads newest-first, user_stats_v thread_count/karma) — without
        // it every profile read seq-scanned the table (found in the 2026-10-17 A/B review; B had idx_posts_author).
        builder.HasIndex(thread => new { thread.OwnerId, thread.CreatedOnUtc, thread.Id })
            .IsDescending(false, true, true)
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_threads_owner_activity");

        // Newest-first search (A/B v5, J6 parity with B's default sort): `search?sort=newest` orders by
        // (created_on_utc DESC, id DESC) and, with a custom plan, walks this index and stops after one page for a
        // common word instead of ranking every match. Partial like the feed index — live rows only.
        builder.HasIndex(thread => new { thread.CreatedOnUtc, thread.Id })
            .IsDescending(true, true)
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_threads_newest");

        // search_tsv (+ its GIN index and trigger) is added by the AddFtsAndViews raw-SQL migration and is
        // deliberately absent from the EF model, so EF never writes it and never tries to drop it.
    }
}
