using Forum.Modules.Social.Domain.Conversations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forum.Modules.Social.Infrastructure.Persistence.Configurations;

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages");
        builder.HasKey(message => message.Id);

        builder.Property(message => message.Body).IsRequired().HasMaxLength(Message.MaxBodyLength);

        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(message => message.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        // The keyset history scan: a conversation's messages newest-first, ULID id IS the creation order.
        // Tombstones stay in history, so no is_deleted filter here.
        builder.HasIndex(message => new { message.ConversationId, message.Id })
            .IsDescending(false, true)
            .HasDatabaseName("ix_messages_history");
    }
}
