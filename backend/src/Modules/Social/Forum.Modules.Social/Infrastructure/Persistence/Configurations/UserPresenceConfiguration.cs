using Forum.Modules.Social.Domain.Presence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forum.Modules.Social.Infrastructure.Persistence.Configurations;

internal sealed class UserPresenceConfiguration : IEntityTypeConfiguration<UserPresence>
{
    public void Configure(EntityTypeBuilder<UserPresence> builder)
    {
        builder.ToTable("user_presence");
        builder.HasKey(presence => presence.UserId);
        // One row per user, upserted in place (INSERT ... ON CONFLICT); status derives from heartbeat age at
        // read time — no status column exists to go stale.
    }
}
