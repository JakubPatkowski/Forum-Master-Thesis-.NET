using FluentValidation;

using Forum.Common.Cqrs;
using Forum.Modules.Identity.Application.Abstractions;
using Forum.Modules.Identity.Application.Validation;
using Forum.Modules.Identity.Domain.Users;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Identity.Application.Account;

/// <summary>Renames the caller's own account. Same rules and uniqueness check as registration.</summary>
internal sealed record ChangeUsernameCommand(Ulid UserId, string Username) : ICommand;

internal sealed class ChangeUsernameCommandValidator : AbstractValidator<ChangeUsernameCommand>
{
    public ChangeUsernameCommandValidator()
    {
        // Mirrors RegisterUserCommandValidator exactly — one place to relax, two to update.
        RuleFor(static command => command.Username)
            .NotEmpty().Length(3, 32)
            .Matches("^[A-Za-z0-9_.-]+$").WithMessage("Username may only contain letters, digits, '.', '_' and '-'.");
    }
}

internal sealed class ChangeUsernameCommandHandler : ICommandHandler<ChangeUsernameCommand>
{
    private readonly IValidator<ChangeUsernameCommand> _validator;
    private readonly IUserRepository _users;
    private readonly IUnitOfWork _unitOfWork;

    public ChangeUsernameCommandHandler(
        IValidator<ChangeUsernameCommand> validator, IUserRepository users, IUnitOfWork unitOfWork)
    {
        _validator = validator;
        _users = users;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(ChangeUsernameCommand command, CancellationToken cancellationToken)
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

        // A pure case change of one's own name is allowed — the uniqueness index would only
        // collide with the user's own row.
        var usernameLc = command.Username.Trim().ToLowerInvariant();
        if (usernameLc != user.UsernameLc && await _users.UsernameExistsAsync(usernameLc, cancellationToken))
        {
            return Result.Failure(UserErrors.UsernameTaken);
        }

        user.ChangeUsername(command.Username);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
