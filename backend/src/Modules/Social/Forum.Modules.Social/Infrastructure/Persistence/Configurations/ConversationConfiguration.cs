using Forum.Modules.Social.Domain.Conversations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forum.Modules.Social.Infrastructure.Persistence.Configurations;

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("conversations");
        builder.HasKey(conversation => conversation.Id);

        builder.Property(conversation => conversation.Type)
            .IsRequired()
            .HasMaxLength(16)
            .IsUnicode(false)
            .HasConversion(
                static value => value.ToString().ToLowerInvariant(),
                static value => Enum.Parse<ConversationType>(value, ignoreCase: true));

        // "{loUlid}:{hiUlid}" — 26 + 1 + 26.
        builder.Property(conversation => conversation.DirectKey).HasMaxLength(53).IsUnicode(false);

        // One Direct conversation per pair; the losing side of a concurrent open re-reads the winner.
        builder.HasIndex(conversation => conversation.DirectKey)
            .IsUnique()
            .HasFilter("direct_key IS NOT NULL")
            .HasDatabaseName("ux_conversations_direct_key");
    }
}
