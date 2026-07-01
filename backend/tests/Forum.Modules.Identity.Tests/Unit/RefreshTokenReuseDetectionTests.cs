using Forum.Common.Security;
using Forum.Modules.Identity.Application.Abstractions;
using Forum.Modules.Identity.Application.Authentication;
using Forum.Modules.Identity.Domain.Tokens;

using Microsoft.Extensions.Options;

using NSubstitute;

using Shouldly;

using Xunit;

namespace Forum.Modules.Identity.Tests.Unit;

/// <summary>The security-critical path: presenting an already-rotated token must revoke the whole family.</summary>
public sealed class RefreshTokenReuseDetectionTests
{
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IRefreshTokenRepository _refreshTokens = Substitute.For<IRefreshTokenRepository>();
    private readonly IAuthorizationStore _authorization = Substitute.For<IAuthorizationStore>();
    private readonly IJwtTokenService _jwt = Substitute.For<IJwtTokenService>();
    private readonly IRefreshTokenService _refreshTokenService = Substitute.For<IRefreshTokenService>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    private RefreshTokenCommandHandler CreateHandler() => new(
        _users, _refreshTokens, _authorization, _jwt, _refreshTokenService, _unitOfWork,
        TimeProvider.System, Options.Create(new JwtOptions()));

    [Fact]
    public async Task Presenting_a_rotated_token_revokes_the_family_and_fails()
    {
        var alreadyRotated = RefreshToken.IssueNew(
            Ulid.NewUlid(), "stored-hash", DateTimeOffset.UtcNow.AddDays(14), DateTimeOffset.UtcNow, null, null);
        alreadyRotated.Rotate(Ulid.NewUlid());

        _refreshTokenService.Hash("presented").Returns("stored-hash");
        _refreshTokens.GetByHashAsync("stored-hash", Arg.Any<CancellationToken>()).Returns(alreadyRotated);

        var result = await CreateHandler().Handle(
            new RefreshTokenCommand("presented", null, null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(AuthErrors.InvalidRefreshToken);
        await _refreshTokens.Received(1).RevokeFamilyAsync(alreadyRotated.FamilyId, Arg.Any<CancellationToken>());
        await _unitOfWork.Received().SaveChangesAsync(Arg.Any<CancellationToken>());
        _jwt.DidNotReceiveWithAnyArgs().Issue(default!, default!);
    }

    [Fact]
    public async Task An_unknown_token_fails_without_touching_any_family()
    {
        _refreshTokenService.Hash("presented").Returns("stored-hash");
        _refreshTokens.GetByHashAsync("stored-hash", Arg.Any<CancellationToken>()).Returns((RefreshToken?)null);

        var result = await CreateHandler().Handle(
            new RefreshTokenCommand("presented", null, null), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(AuthErrors.InvalidRefreshToken);
        await _refreshTokens.DidNotReceiveWithAnyArgs().RevokeFamilyAsync(default, default);
    }
}
