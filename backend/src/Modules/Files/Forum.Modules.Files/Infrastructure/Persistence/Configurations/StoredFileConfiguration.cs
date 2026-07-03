using Forum.Modules.Files.Domain.Files;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forum.Modules.Files.Infrastructure.Persistence.Configurations;

internal sealed class StoredFileConfiguration : IEntityTypeConfiguration<StoredFile>
{
    public void Configure(EntityTypeBuilder<StoredFile> builder)
    {
        builder.ToTable("files");
        builder.HasKey(file => file.Id);

        builder.Property(file => file.Bucket).IsRequired().HasMaxLength(63); // S3 bucket-name limit.
        builder.Property(file => file.ObjectKey).IsRequired().HasMaxLength(256);
        builder.HasIndex(file => new { file.Bucket, file.ObjectKey }).IsUnique();

        builder.Property(file => file.ContentType).IsRequired().HasMaxLength(128);

        builder.Property(file => file.Status)
            .IsRequired()
            .HasMaxLength(16)
            .HasConversion(static value => ToDb(value), static value => FromDb(value));

        builder.Property(file => file.OwnerId).HasColumnName("uploaded_by");

        // The attachments collection is aggregate-internal; EF must hydrate the backing field.
        builder.HasMany(file => file.Attachments)
            .WithOne()
            .HasForeignKey(attachment => attachment.FileId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(file => file.Attachments).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Partial indexes matching exactly what the orphan sweep scans.
        builder.HasIndex(file => file.CreatedOnUtc)
            .HasDatabaseName("ix_files_pending_sweep")
            .HasFilter("status = 'pending'");
        builder.HasIndex(file => file.CommittedOnUtc)
            .HasDatabaseName("ix_files_committed_sweep")
            .HasFilter("status = 'committed'");
    }

    private static string ToDb(FileStatus status) => status switch
    {
        FileStatus.Pending => "pending",
        FileStatus.Committed => "committed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown file status."),
    };

    private static FileStatus FromDb(string value) => value switch
    {
        "pending" => FileStatus.Pending,
        "committed" => FileStatus.Committed,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown file status."),
    };
}
