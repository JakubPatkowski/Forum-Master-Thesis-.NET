using Forum.Modules.Social.Domain.Groups;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forum.Modules.Social.Infrastructure.Persistence.Configurations;

internal sealed class GroupMembershipConfiguration : IEntityTypeConfiguration<GroupMembership>
{
    public void Configure(EntityTypeBuilder<GroupMembership> builder)
    {
        builder.ToTable("group_memberships");
        builder.HasKey(membership => new { membership.GroupId, membership.UserId });

        builder.HasOne<Group>()
            .WithMany()
            .HasForeignKey(membership => membership.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // "My groups" — the directory's mine-filter entry point.
        builder.HasIndex(membership => membership.UserId).HasDatabaseName("ix_group_memberships_user");
    }
}
