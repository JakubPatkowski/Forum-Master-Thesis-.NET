using Forum.Modules.Content.Domain.Threads;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forum.Modules.Content.Infrastructure.Persistence.Configurations;

internal sealed class ThreadTagConfiguration : IEntityTypeConfiguration<ThreadTag>
{
    public void Configure(EntityTypeBuilder<ThreadTag> builder)
    {
        builder.ToTable("thread_tags");
        builder.HasKey(threadTag => new { threadTag.ThreadId, threadTag.TagId });

        builder.HasOne<Thread>()
            .WithMany()
            .HasForeignKey(threadTag => threadTag.ThreadId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Tag>()
            .WithMany()
            .HasForeignKey(threadTag => threadTag.TagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
