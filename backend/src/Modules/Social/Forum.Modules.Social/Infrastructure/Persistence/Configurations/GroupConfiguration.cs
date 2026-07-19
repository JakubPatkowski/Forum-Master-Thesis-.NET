using Forum.Modules.Social.Domain.Groups;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forum.Modules.Social.Infrastructure.Persistence.Configurations;

internal sealed class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.ToTable("groups");
        builder.HasKey(group => group.Id);

        builder.Property(group => group.Name).IsRequired().HasMaxLength(Group.MaxNameLength);
        builder.Property(group => group.Description).IsRequired().HasMaxLength(Group.MaxDescriptionLength);

        builder.Property(group => group.Visibility)
            .IsRequired()
            .HasMaxLength(16)
            .IsUnicode(false)
            .HasConversion(
                static value => value.ToString().ToLowerInvariant(),
                static value => Enum.Parse<GroupVisibility>(value, ignoreCase: true));

        builder.HasIndex(group => group.OwnerId).HasDatabaseName("ix_groups_owner");

        // The public directory scan: live public groups, newest first (id embeds creation order).
        builder.HasIndex(group => group.Id)
            .IsDescending(true)
            .HasFilter("visibility = 'public' AND is_deleted = false")
            .HasDatabaseName("ix_groups_public_directory");
    }
}
