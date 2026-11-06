using Forum.Modules.Content.Domain.Comments;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forum.Modules.Content.Infrastructure.Persistence.Configurations;

internal sealed class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.ToTable("comments");
        builder.HasKey(comment => comment.Id);

        builder.Property(comment => comment.Body).IsRequired().HasMaxLength(10_000);

        // Six 26-char ULIDs + five dots = the longest path at the depth cap.
        builder.Property(comment => comment.Path).IsRequired().HasMaxLength(161).IsUnicode(false);

        builder.HasOne<Thread>()
            .WithMany()
            .HasForeignKey(comment => comment.ThreadId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Comment>()
            .WithMany()
            .HasForeignKey(comment => comment.ParentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Tree reads are ORDER BY path within a thread — one index serves both the filter and the order.
        builder.HasIndex(comment => new { comment.ThreadId, comment.Path })
            .HasDatabaseName("ix_comments_thread_path");

        // Owner activity (profile comments newest-first, user_stats_v comment_count/karma) — same gap as threads.
        builder.HasIndex(comment => new { comment.OwnerId, comment.CreatedOnUtc, comment.Id })
            .IsDescending(false, true, true)
            .HasFilter("is_deleted = false")
            .HasDatabaseName("ix_comments_owner_activity");
    }
}
