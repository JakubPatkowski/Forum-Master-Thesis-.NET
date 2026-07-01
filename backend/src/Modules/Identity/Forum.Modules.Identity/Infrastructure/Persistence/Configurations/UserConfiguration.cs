using Forum.Modules.Identity.Domain.Users;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forum.Modules.Identity.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(user => user.Id);

        builder.Property(user => user.Username).IsRequired().HasMaxLength(32);

        builder.Property(user => user.UsernameLc).IsRequired().HasMaxLength(32);
        builder.HasIndex(user => user.UsernameLc).IsUnique();

        // citext gives case-insensitive uniqueness without a separate lower-cased column.
        builder.Property(user => user.Email).IsRequired().HasColumnType("citext");
        builder.HasIndex(user => user.Email).IsUnique();

        builder.Property(user => user.DisplayName).IsRequired().HasMaxLength(64);
        builder.Property(user => user.PasswordHash).IsRequired();

        builder.Property(user => user.Status)
            .IsRequired()
            .HasMaxLength(24)
            .HasConversion(value => ToDb(value), value => FromDb(value));
        builder.HasIndex(user => user.Status);
    }

    private static string ToDb(UserStatus status) => status switch
    {
        UserStatus.Active => "active",
        UserStatus.Blocked => "blocked",
        UserStatus.PendingVerification => "pending_verification",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown user status."),
    };

    private static UserStatus FromDb(string value) => value switch
    {
        "active" => UserStatus.Active,
        "blocked" => UserStatus.Blocked,
        "pending_verification" => UserStatus.PendingVerification,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown user status."),
    };
}
