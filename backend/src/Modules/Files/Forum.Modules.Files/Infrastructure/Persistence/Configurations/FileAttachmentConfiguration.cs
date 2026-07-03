using Forum.Modules.Files.Application;
using Forum.Modules.Files.Domain.Files;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Forum.Modules.Files.Infrastructure.Persistence.Configurations;

internal sealed class FileAttachmentConfiguration : IEntityTypeConfiguration<FileAttachment>
{
    public void Configure(EntityTypeBuilder<FileAttachment> builder)
    {
        builder.ToTable("file_attachments");
        builder.HasKey(attachment => new { attachment.FileId, attachment.TargetType, attachment.TargetId });

        builder.Property(attachment => attachment.TargetType)
            .IsRequired()
            .HasMaxLength(16)
            .HasConversion(
                static value => FileTargets.ToWire(value),
                static value => Parse(value));

        // The "which files hang on this object?" lookup — list reads and the deletion-event consumers.
        builder.HasIndex(attachment => new { attachment.TargetType, attachment.TargetId })
            .HasDatabaseName("ix_attach_target");
    }

    private static FileTargetType Parse(string value) =>
        FileTargets.TryParse(value, out var target)
            ? target
            : throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown target type.");
}
