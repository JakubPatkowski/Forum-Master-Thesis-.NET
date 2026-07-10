using FluentValidation;

using Forum.Common.Cqrs;
using Forum.Modules.Identity.Application.Abstractions;
using Forum.Modules.Identity.Application.Validation;
using Forum.Modules.Identity.Domain.Users;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Identity.Application.Account;

/// <summary>
/// Replaces the caller's own email. Requires the current password as confirmation — a hijacked
/// access token alone must not be able to redirect account recovery. There is no verification
/// mail flow anywhere in this app (registration has none either), so none is invented here.
/// </summary>
internal sealed record ChangeEmailCommand(Ulid UserId, string Email, string CurrentPassword) : ICommand;

internal sealed class ChangeEmailCommandValidator : AbstractValidator<ChangeEmailCommand>
{
    public ChangeEmailCommandValidator()
    {
        RuleFor(static command => command.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(static command => command.CurrentPassword).NotEmpty();
    }
}

internal sealed class ChangeEmailCommandHandler : ICommandHandler<ChangeEmailCommand>
{
    private readonly IValidator<ChangeEmailCommand> _validator;
    private readonly IUserRepository _users;
    private readonly IPasswordVerifier _passwordVerifier;
    private readonly IUnitOfWork _unitOfWork;

    public ChangeEmailCommandHandler(
        IValidator<ChangeEmailCommand> validator,
        IUserRepository users,
        IPasswordVerifier passwordVerifier,
        IUnitOfWork unitOfWork)
    {
        _validator = validator;
        _users = users;
        _passwordVerifier = passwordVerifier;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ChangeEmailCommand command, CancellationToken cancellationToken)
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

        // Keeping one's own email (any casing — the column is citext) is a no-op, not a conflict.
        var email = command.Email.Trim();
        if (!string.Equals(email, user.Email, StringComparison.OrdinalIgnoreCase)
            && await _users.EmailExistsAsync(email, cancellationToken))
        {
            return Result.Failure(UserErrors.EmailTaken);
        }

        user.ChangeEmail(email);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
