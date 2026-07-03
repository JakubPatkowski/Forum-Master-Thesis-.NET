using Forum.Common.Security;
using Forum.Infrastructure.Storage;
using Forum.Modules.Files.Application;
using Forum.Modules.Files.Application.Abstractions;
using Forum.Modules.Files.Application.Files;
using Forum.Modules.Files.Contracts.IntegrationEvents;
using Forum.Modules.Files.Domain.Files;

using Microsoft.Extensions.Options;

using NSubstitute;

using Shouldly;

using Xunit;

namespace Forum.Modules.Files.Tests.Unit;

public sealed class CommitUploadHandlerTests
{
    private readonly IStoredFileRepository _files = Substitute.For<IStoredFileRepository>();
    private readonly IObjectStorage _storage = Substitute.For<IObjectStorage>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IOutboxWriter _outbox = Substitute.For<IOutboxWriter>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly Ulid _userId = Ulid.NewUlid();

    private CommitUploadCommandHandler CreateHandler() => new(
        _files, _storage, _currentUser, _outbox, _unitOfWork, TimeProvider.System,
        Options.Create(new FilesOptions()));

    private StoredFile SetUpPendingFile(string contentType, long sizeBytes)
    {
        _currentUser.Id.Returns(_userId);
        var file = StoredFile.Create("forum", contentType, sizeBytes, _userId);
        _files.GetByIdAsync(file.Id, Arg.Any<CancellationToken>()).Returns(file);
        return file;
    }

    [Fact]
    public async Task Commit_fails_404_when_the_file_does_not_exist()
    {
        _currentUser.Id.Returns(_userId);
        _files.GetByIdAsync(Arg.Any<Ulid>(), Arg.Any<CancellationToken>()).Returns((StoredFile?)null);

        var result = await CreateHandler().Handle(new CommitUploadCommand(Ulid.NewUlid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(FileErrors.NotFound);
    }

    [Fact]
    public async Task Only_the_uploader_may_commit()
    {
        var file = SetUpPendingFile("image/png", 100);
        _currentUser.Id.Returns(Ulid.NewUlid()); // someone else

        var result = await CreateHandler().Handle(new CommitUploadCommand(file.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(FileErrors.NotOwner);
    }

    [Fact]
    public async Task Commit_conflicts_when_no_object_was_uploaded()
    {
        var file = SetUpPendingFile("image/png", 100);
        _storage.StatAsync(file.ObjectKey, Arg.Any<CancellationToken>()).Returns((ObjectStatResult?)null);

        var result = await CreateHandler().Handle(new CommitUploadCommand(file.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(FileErrors.NotUploaded);
    }

    [Fact]
    public async Task Commit_rejects_a_real_size_that_differs_from_the_declared_size()
    {
        var png = TestImages.Png(10, 10);
        var file = SetUpPendingFile("image/png", sizeBytes: 999);
        _storage.StatAsync(file.ObjectKey, Arg.Any<CancellationToken>())
            .Returns(new ObjectStatResult(png.Length, "image/png", "etag"));
        _storage.ReadRangeAsync(file.ObjectKey, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(png);

        var result = await CreateHandler().Handle(new CommitUploadCommand(file.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(FileErrors.SizeMismatch);
        file.Status.ShouldBe(FileStatus.Pending);
    }

    [Fact]
    public async Task Commit_rejects_bytes_whose_sniffed_type_differs_from_the_declared_type()
    {
        var gif = TestImages.Gif(10, 10); // declared png, uploaded gif
        var file = SetUpPendingFile("image/png", gif.Length);
        _storage.StatAsync(file.ObjectKey, Arg.Any<CancellationToken>())
            .Returns(new ObjectStatResult(gif.Length, "image/png", "etag"));
        _storage.ReadRangeAsync(file.ObjectKey, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(gif);

        var result = await CreateHandler().Handle(new CommitUploadCommand(file.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(FileErrors.TypeMismatch);
    }

    [Fact]
    public async Task Commit_rejects_bytes_that_are_not_a_decodable_image()
    {
        var garbage = "definitely not an image"u8.ToArray();
        var file = SetUpPendingFile("image/png", garbage.Length);
        _storage.StatAsync(file.ObjectKey, Arg.Any<CancellationToken>())
            .Returns(new ObjectStatResult(garbage.Length, "image/png", "etag"));
        _storage.ReadRangeAsync(file.ObjectKey, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(garbage);

        var result = await CreateHandler().Handle(new CommitUploadCommand(file.Id), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(FileErrors.NotADecodableImage);
    }

    [Fact]
    public async Task A_verified_commit_records_dimensions_and_writes_the_outbox_event()
    {
        var png = TestImages.Png(640, 480);
        var file = SetUpPendingFile("image/png", png.Length);
        _storage.StatAsync(file.ObjectKey, Arg.Any<CancellationToken>())
            .Returns(new ObjectStatResult(png.Length, "image/png", "etag"));
        _storage.ReadRangeAsync(file.ObjectKey, Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(png);

        var result = await CreateHandler().Handle(new CommitUploadCommand(file.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Width.ShouldBe(640);
        result.Value.Height.ShouldBe(480);
        file.Status.ShouldBe(FileStatus.Committed);
        _outbox.Received(1).Enqueue(Arg.Is<FileCommittedIntegrationEvent>(committed =>
            committed.FileId == file.Id && committed.Width == 640 && committed.Height == 480));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Recommitting_a_committed_file_is_an_idempotent_success_that_skips_storage()
    {
        var file = SetUpPendingFile("image/png", 100);
        file.Commit(100, "image/png", 10, 10, DateTimeOffset.UtcNow);

        var result = await CreateHandler().Handle(new CommitUploadCommand(file.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Width.ShouldBe(10);
        await _storage.DidNotReceiveWithAnyArgs().StatAsync(default!, default);
        await _unitOfWork.DidNotReceiveWithAnyArgs().SaveChangesAsync(default);
    }
}
