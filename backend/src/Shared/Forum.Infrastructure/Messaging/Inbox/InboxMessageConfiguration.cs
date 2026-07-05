using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forum.Infrastructure.Messaging.Inbox;

/// <summary>Shared mapping for the per-module <c>inbox_messages</c> table. Modules apply this in their DbContext's OnModelCreating.</summary>
public sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("inbox_messages");
        builder.HasKey(message => message.Id);
    }
}
