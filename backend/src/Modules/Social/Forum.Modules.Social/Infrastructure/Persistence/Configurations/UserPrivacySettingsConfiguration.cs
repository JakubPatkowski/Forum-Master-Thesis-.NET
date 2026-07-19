using Forum.Modules.Social.Domain.Privacy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forum.Modules.Social.Infrastructure.Persistence.Configurations;

internal sealed class UserPrivacySettingsConfiguration : IEntityTypeConfiguration<UserPrivacySettings>
{
    public void Configure(EntityTypeBuilder<UserPrivacySettings> builder)
    {
        builder.ToTable("user_privacy_settings");
        builder.HasKey(settings => settings.UserId);

        builder.Property(settings => settings.FriendRequests).HasAudienceConversion();
        builder.Property(settings => settings.Messages).HasAudienceConversion();
        builder.Property(settings => settings.GroupInvites).HasAudienceConversion();
    }
}

file static class AudienceConversion
{
    public static void HasAudienceConversion(this PropertyBuilder<PrivacyAudience> property) =>
        property
            .IsRequired()
            .HasMaxLength(16)
            .IsUnicode(false)
            .HasConversion(
                static value => ToWire(value),
                static value => Parse(value));

    private static string ToWire(PrivacyAudience audience) => audience switch
    {
        PrivacyAudience.Everyone => "everyone",
        PrivacyAudience.Friends => "friends",
        _ => "no_one",
    };

    private static PrivacyAudience Parse(string value) => value switch
    {
        "everyone" => PrivacyAudience.Everyone,
        "friends" => PrivacyAudience.Friends,
        _ => PrivacyAudience.NoOne,
    };
}
