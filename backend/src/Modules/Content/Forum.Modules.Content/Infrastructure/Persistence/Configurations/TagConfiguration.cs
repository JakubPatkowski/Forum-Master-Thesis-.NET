using Forum.Modules.Content.Domain.Threads;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forum.Modules.Content.Infrastructure.Persistence.Configurations;

internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("tags");
        builder.HasKey(tag => tag.Id);

        builder.Property(tag => tag.Slug).IsRequired().HasMaxLength(32);
        builder.HasIndex(tag => tag.Slug).IsUnique();

        builder.Property(tag => tag.Name).IsRequired().HasMaxLength(64);
    }
}
