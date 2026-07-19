using Forum.Modules.Social.Domain.Friendships;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forum.Modules.Social.Infrastructure.Persistence.Configurations;

internal sealed class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    public void Configure(EntityTypeBuilder<Friendship> builder)
    {
        builder.ToTable("friendships");
        builder.HasKey(friendship => friendship.Id);

        builder.Property(friendship => friendship.Status)
            .IsRequired()
            .HasMaxLength(16)
            .IsUnicode(false)
            .HasConversion(
                static value => value.ToString().ToLowerInvariant(),
                static value => Enum.Parse<FriendshipStatus>(value, ignoreCase: true));

        // Per-side lookups (my incoming / my outgoing / involving-me cleanups).
        builder.HasIndex(friendship => friendship.RequesterId).HasDatabaseName("ix_friendships_requester");
        builder.HasIndex(friendship => friendship.AddresseeId).HasDatabaseName("ix_friendships_addressee");

        // One row per DIRECTED pair via EF; the InitialSocial migration adds the raw-SQL
        // LEAST/GREATEST unique index that collapses both directions to one row per pair.
        builder.HasIndex(friendship => new { friendship.RequesterId, friendship.AddresseeId })
            .IsUnique()
            .HasDatabaseName("ux_friendships_pair_directed");
    }
}
