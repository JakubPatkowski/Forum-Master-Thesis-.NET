using Forum.Modules.Identity.Domain.Tokens;
using Forum.Modules.Identity.Domain.Users;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forum.Modules.Identity.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(token => token.Id);

        builder.Property(token => token.UserId).IsRequired();
        builder.HasIndex(token => token.UserId);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(token => token.FamilyId).IsRequired();
        builder.HasIndex(token => token.FamilyId);

        builder.Property(token => token.TokenHash).IsRequired().HasMaxLength(128);
        builder.HasIndex(token => token.TokenHash).IsUnique();

        builder.Property(token => token.Status)
            .IsRequired()
            .HasMaxLength(16)
            .HasConversion(value => ToDb(value), value => FromDb(value));

        builder.Property(token => token.ExpiresOnUtc).IsRequired();
        builder.Property(token => token.CreatedOnUtc).IsRequired();
        builder.Property(token => token.Ip).HasMaxLength(64);
        builder.Property(token => token.UserAgent).HasMaxLength(512);
    }

    private static string ToDb(RefreshTokenStatus status) => status switch
    {
        RefreshTokenStatus.Active => "active",
        RefreshTokenStatus.Rotated => "rotated",
        RefreshTokenStatus.Revoked => "revoked",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown token status."),
    };

    private static RefreshTokenStatus FromDb(string value) => value switch
    {
        "active" => RefreshTokenStatus.Active,
        "rotated" => RefreshTokenStatus.Rotated,
        "revoked" => RefreshTokenStatus.Revoked,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown token status."),
    };
}
