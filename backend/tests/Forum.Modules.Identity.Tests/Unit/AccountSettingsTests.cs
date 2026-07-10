using Forum.Modules.Identity.Application.Abstractions;
using Forum.Modules.Identity.Application.Account;
using Forum.Modules.Identity.Domain.Users;

using NSubstitute;

using Shouldly;

using Xunit;

namespace Forum.Modules.Identity.Tests.Unit;

/// <summary>Self-service account changes: password confirmation gates, uniqueness, session revocation.</summary>
public sealed class AccountSettingsTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IPasswordVerifier _passwordVerifier = Substitute.For<IPasswordVerifier>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private readonly User _user = User.Register("JakubP", "jakub@example.com", "Jakub", "old-hash");

    public AccountSettingsTests() =>
        _users.GetByIdAsync(_user.Id, Arg.Any<CancellationToken>()).Returns(_user);

    private ChangeUsernameCommandHandler UsernameHandler() =>
        new(new ChangeUsernameCommandValidator(), _users, _unitOfWork);

    private ChangeEmailCommandHandler EmailHandler() =>
        new(new ChangeEmailCommandValidator(), _users, _passwordVerifier, _unitOfWork);

    private ChangePasswordCommandHandler PasswordHandler() => new(
        new ChangePasswordCommandValidator(), _users, _refreshTokens, _passwordVerifier, _passwordHasher, _unitOfWork);

    [Fact]
    public async Task Changing_username_updates_both_forms_and_saves()
    {
        _users.UsernameExistsAsync("newname", Arg.Any<CancellationToken>()).Returns(false);

        var result = await UsernameHandler().Handle(
            new ChangeUsernameCommand(_user.Id, "NewName"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _user.Username.ShouldBe("NewName");
        _user.UsernameLc.ShouldBe("newname");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_taken_username_is_a_conflict()
    {
        _users.UsernameExistsAsync("taken", Arg.Any<CancellationToken>()).Returns(true);

        var result = await UsernameHandler().Handle(
            new ChangeUsernameCommand(_user.Id, "Taken"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.UsernameTaken);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Recasing_your_own_username_skips_the_uniqueness_check()
    {
        _users.UsernameExistsAsync("jakubp", Arg.Any<CancellationToken>()).Returns(true);

        var result = await UsernameHandler().Handle(
            new ChangeUsernameCommand(_user.Id, "jakubp"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _user.Username.ShouldBe("jakubp");
    }

    [Fact]
    public async Task An_invalid_username_fails_validation()
    {
        var result = await UsernameHandler().Handle(
            new ChangeUsernameCommand(_user.Id, "no spaces!"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("validation.failed");
    }

    [Fact]
    public async Task Changing_email_requires_the_correct_current_password()
    {
        _passwordVerifier.Verify("old-hash", "wrong").Returns(false);

        var result = await EmailHandler().Handle(
            new ChangeEmailCommand(_user.Id, "new@example.com", "wrong"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(AccountErrors.InvalidPassword);
        _user.Email.ShouldBe("jakub@example.com");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Changing_email_with_the_correct_password_saves()
    {
        _passwordVerifier.Verify("old-hash", "current").Returns(true);
        _users.EmailExistsAsync("new@example.com", Arg.Any<CancellationToken>()).Returns(false);

        var result = await EmailHandler().Handle(
            new ChangeEmailCommand(_user.Id, "new@example.com", "current"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _user.Email.ShouldBe("new@example.com");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_taken_email_is_a_conflict()
    {
        _passwordVerifier.Verify("old-hash", "current").Returns(true);
        _users.EmailExistsAsync("taken@example.com", Arg.Any<CancellationToken>()).Returns(true);

        var result = await EmailHandler().Handle(
            new ChangeEmailCommand(_user.Id, "taken@example.com", "current"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.EmailTaken);
    }

    [Fact]
    public async Task Changing_password_requires_the_correct_current_password()
    {
        _passwordVerifier.Verify("old-hash", "wrong").Returns(false);

        var result = await PasswordHandler().Handle(
            new ChangePasswordCommand(_user.Id, "wrong", "new-password-1"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(AccountErrors.InvalidPassword);
        _user.PasswordHash.ShouldBe("old-hash");
        await _refreshTokens.DidNotReceiveWithAnyArgs().RevokeAllForUserAsync(default, default);
    }

    [Fact]
    public async Task Changing_password_rehashes_and_revokes_every_session()
    {
        _passwordVerifier.Verify("old-hash", "current").Returns(true);
        _passwordHasher.Hash("new-password-1").Returns("new-hash");

        var result = await PasswordHandler().Handle(
            new ChangePasswordCommand(_user.Id, "current", "new-password-1"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _user.PasswordHash.ShouldBe("new-hash");
        await _refreshTokens.Received(1).RevokeAllForUserAsync(_user.Id, Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_weak_new_password_fails_validation_before_any_verify()
    {
        var result = await PasswordHandler().Handle(
            new ChangePasswordCommand(_user.Id, "current", "short"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("validation.failed");
        _passwordVerifier.DidNotReceiveWithAnyArgs().Verify(default!, default!);
    }
}
