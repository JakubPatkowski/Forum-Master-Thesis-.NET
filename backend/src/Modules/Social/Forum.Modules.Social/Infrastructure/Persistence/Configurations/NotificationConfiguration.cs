using Forum.Modules.Social.Domain.Notifications;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forum.Modules.Social.Infrastructure.Persistence.Configurations;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(notification => notification.Id);

        builder.Property(notification => notification.Kind).IsRequired().HasMaxLength(32).IsUnicode(false);

        // The bell feed: one user's notifications newest-first.
        builder.HasIndex(notification => new { notification.UserId, notification.Id })
            .IsDescending(false, true)
            .HasDatabaseName("ix_notifications_feed");

        // The badge count: unread rows only (partial — the hot set stays tiny as rows get read).
        builder.HasIndex(notification => notification.UserId)
            .HasFilter("is_read = false")
            .HasDatabaseName("ix_notifications_unread");
    }
}
