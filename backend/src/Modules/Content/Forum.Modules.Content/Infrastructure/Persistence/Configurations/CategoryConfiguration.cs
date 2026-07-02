using Forum.Modules.Content.Domain.Categories;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forum.Modules.Content.Infrastructure.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");
        builder.HasKey(category => category.Id);

        builder.Property(category => category.Slug).IsRequired().HasMaxLength(64);
        builder.HasIndex(category => category.Slug).IsUnique();

        builder.Property(category => category.Name).IsRequired().HasMaxLength(128);
        builder.Property(category => category.Description).IsRequired().HasMaxLength(2000);

        builder.Property(category => category.Visibility)
            .IsRequired()
            .HasMaxLength(16)
            .HasConversion(static value => ToDb(value), static value => FromDb(value));
    }

    private static string ToDb(Visibility visibility) => visibility switch
    {
        Visibility.Public => "public",
        Visibility.Private => "private",
        _ => throw new ArgumentOutOfRangeException(nameof(visibility), visibility, "Unknown visibility."),
    };

    private static Visibility FromDb(string value) => value switch
    {
        "public" => Visibility.Public,
        "private" => Visibility.Private,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown visibility."),
    };
}
