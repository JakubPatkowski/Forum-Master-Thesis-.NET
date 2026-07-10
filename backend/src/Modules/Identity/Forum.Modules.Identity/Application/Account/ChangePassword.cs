using FluentValidation;

using Forum.Common.Cqrs;
using Forum.Modules.Identity.Application.Abstractions;
using Forum.Modules.Identity.Application.Validation;
using Forum.Modules.Identity.Domain.Users;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Identity.Application.Account;

/// <summary>
/// Changes the caller's own password: verify the current one (Argon2id, same verifier as login),
/// rehash the new one, and revoke every refresh token — a password change signs out every other
/// session, the same primitive logout-all uses.
/// </summary>
internal sealed record ChangePasswordCommand(Ulid UserId, string CurrentPassword, string NewPassword) : ICommand;

internal sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(static command => command.CurrentPassword).NotEmpty();
        // Same strength rule as RegisterUserCommandValidator.
        RuleFor(static command => command.NewPassword).NotEmpty().Length(8, 128);
    }
}

internal sealed class ChangePasswordCommandHandler : ICommandHandler<ChangePasswordCommand>
{
    private readonly IValidator<ChangePasswordCommand> _validator;
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordVerifier _passwordVerifier;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IUnitOfWork _unitOfWork;

    public ChangePasswordCommandHandler(
        IValidator<ChangePasswordCommand> validator,
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IPasswordVerifier passwordVerifier,
        IPasswordHasher passwordHasher,
        IUnitOfWork unitOfWork)
    {
        _validator = validator;
        _users = users;
        _refreshTokens = refreshTokens;
        _passwordVerifier = passwordVerifier;
        _passwordHasher = passwordHasher;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        if (await _validator.ValidateToErrorAsync(command, cancellationToken) is { } validationError)
        {
            return Result.Failure(validationError);
        }

        var user = await _users.GetByIdAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(UserErrors.NotFound);
        }

        if (!_passwordVerifier.Verify(user.PasswordHash, command.CurrentPassword))
        {
            return Result.Failure(AccountErrors.InvalidPassword);
        }

        user.SetPasswordHash(_passwordHasher.Hash(command.NewPassword));
        await _refreshTokens.RevokeAllForUserAsync(user.Id, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
