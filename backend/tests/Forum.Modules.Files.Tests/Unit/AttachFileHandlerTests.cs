using Forum.Common.Security;
using Forum.Modules.Content.Contracts;
using Forum.Modules.Files.Application;
using Forum.Modules.Files.Application.Abstractions;
using Forum.Modules.Files.Application.Attachments;
using Forum.Modules.Files.Domain.Files;
using Forum.Modules.Social.Contracts;
using Forum.SharedKernel.Results;

using Microsoft.Extensions.Options;

using NSubstitute;

using Shouldly;

using Xunit;

namespace Forum.Modules.Files.Tests.Unit;

public sealed class AttachFileHandlerTests
{
    private static readonly Error TargetForbidden = Error.Forbidden("thread.forbidden", "Not yours.");

    private readonly IStoredFileRepository _files = Substitute.For<IStoredFileRepository>();
    private readonly IContentAuthorization _contentAuthorization = Substitute.For<IContentAuthorization>();
    private readonly ISocialAuthorization _socialAuthorization = Substitute.For<ISocialAuthorization>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly Ulid _userId = Ulid.NewUlid();

    public AttachFileHandlerTests()
    {
        _files.GetAttachedToTargetAsync(Arg.Any<FileTargetType>(), Arg.Any<Ulid>(), Arg.Any<CancellationToken>())
            .Returns([]);
    }

    private AttachFileCommandHandler CreateHandler() => new(
        _files, _contentAuthorization, _socialAuthorization, _currentUser, _unitOfWork, TimeProvider.System,
        Options.Create(new FilesOptions()));

    private StoredFile SetUpCommittedFile(Ulid? owner = null)
    {
        _currentUser.Id.Returns(_userId);
        var file = StoredFile.Create("forum", "image/png", 100, owner ?? _userId);
        file.Commit(100, "image/png", 10, 10, DateTimeOffset.UtcNow);
        _files.GetByIdAsync(file.Id, Arg.Any<CancellationToken>()).Returns(file);
        return file;
    }

    [Fact]
    public async Task Only_the_uploader_may_attach_their_file()
    {
        var file = SetUpCommittedFile(owner: Ulid.NewUlid());

        var result = await CreateHandler().Handle(
            new AttachFileCommand(file.Id, "thread", Ulid.NewUlid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(FileErrors.NotOwner);
    }

    [Fact]
    public async Task A_pending_file_cannot_be_attached()
    {
        _currentUser.Id.Returns(_userId);
        var file = StoredFile.Create("forum", "image/png", 100, _userId);
        _files.GetByIdAsync(file.Id, Arg.Any<CancellationToken>()).Returns(file);

        var result = await CreateHandler().Handle(
            new AttachFileCommand(file.Id, "thread", Ulid.NewUlid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(FileErrors.NotCommitted);
    }

    [Fact]
    public async Task Content_targets_require_contents_authorization_verdict()
    {
        var file = SetUpCommittedFile();
        var threadId = Ulid.NewUlid();
        _contentAuthorization.AuthorizeAttachmentAsync(
                ContentAttachmentTarget.Thread, threadId, _userId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure(TargetForbidden));

        var result = await CreateHandler().Handle(
            new AttachFileCommand(file.Id, "thread", threadId), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(TargetForbidden);
        file.IsAttached.ShouldBeFalse();
    }

    [Fact]
    public async Task An_authorized_thread_attach_links_the_file()
    {
        var file = SetUpCommittedFile();
        var threadId = Ulid.NewUlid();
        _contentAuthorization.AuthorizeAttachmentAsync(
                ContentAttachmentTarget.Thread, threadId, _userId, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _files.CountAttachmentsForTargetAsync(FileTargetType.Thread, threadId, Arg.Any<CancellationToken>())
            .Returns(0);

        var result = await CreateHandler().Handle(
            new AttachFileCommand(file.Id, "thread", threadId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        file.IsAttachedTo(FileTargetType.Thread, threadId).ShouldBeTrue();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_per_target_attachment_cap_is_enforced()
    {
        var file = SetUpCommittedFile();
        var threadId = Ulid.NewUlid();
        _contentAuthorization.AuthorizeAttachmentAsync(
                ContentAttachmentTarget.Thread, threadId, _userId, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _files.CountAttachmentsForTargetAsync(FileTargetType.Thread, threadId, Arg.Any<CancellationToken>())
            .Returns(new FilesOptions().MaxAttachmentsPerTarget);

        var result = await CreateHandler().Handle(
            new AttachFileCommand(file.Id, "thread", threadId), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(FileErrors.TooManyAttachments);
    }

    [Fact]
    public async Task An_avatar_can_only_target_the_requesting_user()
    {
        var file = SetUpCommittedFile();

        var result = await CreateHandler().Handle(
            new AttachFileCommand(file.Id, "avatar", Ulid.NewUlid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(FileErrors.AvatarTargetMismatch);
    }

    [Fact]
    public async Task Attaching_a_new_avatar_replaces_the_previous_one()
    {
        var file = SetUpCommittedFile();
        var oldAvatar = StoredFile.Create("forum", "image/png", 50, _userId);
        oldAvatar.Commit(50, "image/png", 5, 5, DateTimeOffset.UtcNow);
        oldAvatar.Attach(FileTargetType.Avatar, _userId, DateTimeOffset.UtcNow);
        _files.GetAttachedToTargetAsync(FileTargetType.Avatar, _userId, Arg.Any<CancellationToken>())
            .Returns([oldAvatar]);

        var result = await CreateHandler().Handle(
            new AttachFileCommand(file.Id, "avatar", null), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        file.IsAttachedTo(FileTargetType.Avatar, _userId).ShouldBeTrue();
        oldAvatar.IsAttachedTo(FileTargetType.Avatar, _userId).ShouldBeFalse(); // sweep collects it later
    }

    [Fact]
    public async Task Message_targets_require_socials_authorization_verdict()
    {
        var file = SetUpCommittedFile();
        var messageId = Ulid.NewUlid();
        _socialAuthorization.AuthorizeAttachmentAsync(
                SocialAttachmentTarget.Message, messageId, _userId, Arg.Any<CancellationToken>())
            .Returns(Result.Failure(TargetForbidden));

        var result = await CreateHandler().Handle(
            new AttachFileCommand(file.Id, "message", messageId), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(TargetForbidden);
        file.IsAttached.ShouldBeFalse();
    }

    [Fact]
    public async Task An_authorized_message_attach_links_the_file_additively()
    {
        var file = SetUpCommittedFile();
        var messageId = Ulid.NewUlid();
        _socialAuthorization.AuthorizeAttachmentAsync(
                SocialAttachmentTarget.Message, messageId, _userId, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        _files.CountAttachmentsForTargetAsync(FileTargetType.Message, messageId, Arg.Any<CancellationToken>())
            .Returns(0);

        var result = await CreateHandler().Handle(
            new AttachFileCommand(file.Id, "message", messageId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        file.IsAttachedTo(FileTargetType.Message, messageId).ShouldBeTrue();
    }

    [Fact]
    public async Task Attaching_a_new_group_icon_replaces_the_previous_one()
    {
        var file = SetUpCommittedFile();
        var groupId = Ulid.NewUlid();
        _socialAuthorization.AuthorizeAttachmentAsync(
                SocialAttachmentTarget.GroupIcon, groupId, _userId, Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        var oldIcon = StoredFile.Create("forum", "image/png", 50, _userId);
        oldIcon.Commit(50, "image/png", 5, 5, DateTimeOffset.UtcNow);
        oldIcon.Attach(FileTargetType.GroupIcon, groupId, DateTimeOffset.UtcNow);
        _files.GetAttachedToTargetAsync(FileTargetType.GroupIcon, groupId, Arg.Any<CancellationToken>())
            .Returns([oldIcon]);

        var result = await CreateHandler().Handle(
            new AttachFileCommand(file.Id, "group_icon", groupId), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        file.IsAttachedTo(FileTargetType.GroupIcon, groupId).ShouldBeTrue();
        oldIcon.IsAttachedTo(FileTargetType.GroupIcon, groupId).ShouldBeFalse(); // sweep collects it later
    }

    [Fact]
    public async Task An_unknown_target_type_is_rejected()
    {
        var file = SetUpCommittedFile();

        var result = await CreateHandler().Handle(
            new AttachFileCommand(file.Id, "banner", Ulid.NewUlid()), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(FileErrors.InvalidTargetType);
    }
}
