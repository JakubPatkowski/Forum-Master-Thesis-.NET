using Forum.Modules.Engagement.Application;
using Forum.Modules.Engagement.Domain.Reactions;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forum.Modules.Engagement.Infrastructure.Persistence.Configurations;

internal sealed class ReactionConfiguration : IEntityTypeConfiguration<Reaction>
{
    public void Configure(EntityTypeBuilder<Reaction> builder)
    {
        builder.ToTable("reactions");

        // reaction_type sits in the key on purpose: a later phase can let one user place several distinct
        // reaction kinds on the same target without a schema rewrite.
        builder.HasKey(reaction =>
            new { reaction.UserId, reaction.TargetType, reaction.TargetId, reaction.ReactionType });

        builder.Property(reaction => reaction.TargetType)
            .IsRequired()
            .HasMaxLength(16)
            .IsUnicode(false)
            .HasConversion(
                static value => ReactionTargets.ToWire(value),
                static value => Parse(value));

        builder.Property(reaction => reaction.ReactionType)
            .IsRequired()
            .HasMaxLength(32)
            .IsUnicode(false);

        builder.Property(reaction => reaction.Value)
            .IsRequired()
            .HasDefaultValue((short)1);

        // The "who reacted to this object?" lookup — the deletion-cascade consumers and any recount audit.
        builder.HasIndex(reaction => new { reaction.TargetType, reaction.TargetId, reaction.ReactionType })
            .HasDatabaseName("ix_reactions_target");
    }

    private static ReactionTargetType Parse(string value) =>
        ReactionTargets.TryParse(value, out var target)
            ? target
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown target type.");
}
