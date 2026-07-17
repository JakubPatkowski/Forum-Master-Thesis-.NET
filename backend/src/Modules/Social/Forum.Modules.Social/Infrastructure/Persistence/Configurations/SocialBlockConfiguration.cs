using Forum.Modules.Social.Domain.Friendships;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forum.Modules.Social.Infrastructure.Persistence.Configurations;

internal sealed class SocialBlockConfiguration : IEntityTypeConfiguration<SocialBlock>
{
    public void Configure(EntityTypeBuilder<SocialBlock> builder)
    {
        builder.ToTable("social_blocks");
        builder.HasKey(block => new { block.BlockerId, block.BlockedId });

        // The reverse arm of the either-direction gate ("does the target block me?").
        builder.HasIndex(block => block.BlockedId).HasDatabaseName("ix_social_blocks_blocked");
    }
}
