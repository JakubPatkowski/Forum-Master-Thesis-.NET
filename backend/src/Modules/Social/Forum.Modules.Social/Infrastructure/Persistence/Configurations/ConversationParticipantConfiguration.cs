using Forum.Modules.Social.Domain.Conversations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forum.Modules.Social.Infrastructure.Persistence.Configurations;

internal sealed class ConversationParticipantConfiguration : IEntityTypeConfiguration<ConversationParticipant>
{
    public void Configure(EntityTypeBuilder<ConversationParticipant> builder)
    {
        builder.ToTable("conversation_participants");
        builder.HasKey(participant => new { participant.ConversationId, participant.UserId });

        builder.HasOne<Conversation>()
            .WithMany()
            .HasForeignKey(participant => participant.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        // The conversation-list entry point: one user's seats (active filter applied in the view/queries).
        builder.HasIndex(participant => participant.UserId).HasDatabaseName("ix_conversation_participants_user");
    }
}
