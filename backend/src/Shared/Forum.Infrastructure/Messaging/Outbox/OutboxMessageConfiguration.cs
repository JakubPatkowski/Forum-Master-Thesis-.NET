using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forum.Infrastructure.Messaging.Outbox;

/// <summary>Shared mapping for the per-module <c>outbox_messages</c> table. Modules apply this in their DbContext's OnModelCreating.</summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Type).HasMaxLength(512);
        builder.Property(message => message.Payload).HasColumnType("jsonb");
        builder.Property(message => message.CorrelationId).HasMaxLength(64);

        // The relay polls for unprocessed rows; index the predicate it scans.
        builder.HasIndex(message => message.ProcessedOnUtc);
    }
}
