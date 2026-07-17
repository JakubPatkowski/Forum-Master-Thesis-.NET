using Forum.Modules.Social.Domain.Groups;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forum.Modules.Social.Infrastructure.Persistence.Configurations;

internal sealed class GroupInviteConfiguration : IEntityTypeConfiguration<GroupInvite>
{
    public void Configure(EntityTypeBuilder<GroupInvite> builder)
    {
        builder.ToTable("group_invites");
        builder.HasKey(invite => invite.Id);

        builder.HasOne<Group>()
            .WithMany()
            .HasForeignKey(invite => invite.GroupId)
            .OnDelete(DeleteBehavior.Cascade);

        // Only pending invites persist, so one live invite per (group, invitee) is a plain unique index.
        builder.HasIndex(invite => new { invite.GroupId, invite.InvitedUserId })
            .IsUnique()
            .HasDatabaseName("ux_group_invites_pending");

        builder.HasIndex(invite => invite.InvitedUserId).HasDatabaseName("ix_group_invites_invitee");
        builder.HasIndex(invite => invite.InvitedBy).HasDatabaseName("ix_group_invites_inviter");
    }
}
