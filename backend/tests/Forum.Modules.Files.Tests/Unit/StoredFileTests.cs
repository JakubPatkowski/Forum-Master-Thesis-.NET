using Forum.Modules.Files.Domain.Files;

using Shouldly;

using Xunit;

namespace Forum.Modules.Files.Tests.Unit;

public sealed class StoredFileTests
{
    private static StoredFile NewPending(string contentType = "image/png", long sizeBytes = 100) =>
        StoredFile.Create("forum", contentType, sizeBytes, Ulid.NewUlid());

    private static StoredFile NewCommitted(string contentType = "image/png", long sizeBytes = 100)
    {
        var file = NewPending(contentType, sizeBytes);
        file.Commit(sizeBytes, contentType, 10, 10, DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
        return file;
    }

    [Fact]
    public void Create_starts_pending_with_a_month_sharded_key_and_normalized_type()
    {
        var owner = Ulid.NewUlid();

        var file = StoredFile.Create("forum", "IMAGE/PNG", 1234, owner);

        file.Status.ShouldBe(FileStatus.Pending);
        file.ContentType.ShouldBe("image/png");
        file.SizeBytes.ShouldBe(1234);
        file.OwnerId.ShouldBe(owner);
        file.Bucket.ShouldBe("forum");
        file.ObjectKey.ShouldBe($"{file.Id.Time:yyyy'/'MM}/{file.Id}");
        file.IsAttached.ShouldBeFalse();
    }

    [Fact]
    public void Commit_verifies_and_records_the_dimensions()
    {
        var file = NewPending(sizeBytes: 100);
        var now = DateTimeOffset.UtcNow;

        var result = file.Commit(100, "image/png", 640, 480, now);

        result.IsSuccess.ShouldBeTrue();
        file.Status.ShouldBe(FileStatus.Committed);
        file.Width.ShouldBe(640);
        file.Height.ShouldBe(480);
        file.CommittedOnUtc.ShouldBe(now);
    }

    [Fact]
    public void Commit_rejects_a_size_mismatch()
    {
        var file = NewPending(sizeBytes: 100);

        var result = file.Commit(99, "image/png", 640, 480, DateTimeOffset.UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(FileErrors.SizeMismatch);
        file.Status.ShouldBe(FileStatus.Pending);
    }

    [Fact]
    public void Commit_rejects_a_content_type_mismatch()
    {
        var file = NewPending(contentType: "image/png", sizeBytes: 100);

        var result = file.Commit(100, "image/gif", 640, 480, DateTimeOffset.UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(FileErrors.TypeMismatch);
        file.Status.ShouldBe(FileStatus.Pending);
    }

    [Fact]
    public void Committing_twice_fails()
    {
        var file = NewCommitted();

        var result = file.Commit(100, "image/png", 10, 10, DateTimeOffset.UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(FileErrors.AlreadyCommitted);
    }

    [Fact]
    public void A_pending_file_cannot_be_attached()
    {
        var file = NewPending();

        var result = file.Attach(FileTargetType.Thread, Ulid.NewUlid(), DateTimeOffset.UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(FileErrors.NotCommitted);
    }

    [Fact]
    public void Attach_is_idempotent_per_target()
    {
        var file = NewCommitted();
        var threadId = Ulid.NewUlid();

        file.Attach(FileTargetType.Thread, threadId, DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();
        file.Attach(FileTargetType.Thread, threadId, DateTimeOffset.UtcNow).IsSuccess.ShouldBeTrue();

        file.Attachments.ShouldHaveSingleItem();
        file.IsAttachedTo(FileTargetType.Thread, threadId).ShouldBeTrue();
    }

    [Fact]
    public void A_file_can_link_to_multiple_targets_and_detach_only_one()
    {
        var file = NewCommitted();
        var threadId = Ulid.NewUlid();
        var commentId = Ulid.NewUlid();

        file.Attach(FileTargetType.Thread, threadId, DateTimeOffset.UtcNow);
        file.Attach(FileTargetType.Comment, commentId, DateTimeOffset.UtcNow);

        file.Detach(FileTargetType.Thread, threadId).IsSuccess.ShouldBeTrue();

        file.IsAttachedTo(FileTargetType.Thread, threadId).ShouldBeFalse();
        file.IsAttachedTo(FileTargetType.Comment, commentId).ShouldBeTrue();
        file.IsAttached.ShouldBeTrue();
    }

    [Fact]
    public void Detaching_a_missing_link_is_a_noop_success()
    {
        var file = NewCommitted();

        var result = file.Detach(FileTargetType.Thread, Ulid.NewUlid());

        result.IsSuccess.ShouldBeTrue();
        file.IsAttached.ShouldBeFalse();
    }
}
