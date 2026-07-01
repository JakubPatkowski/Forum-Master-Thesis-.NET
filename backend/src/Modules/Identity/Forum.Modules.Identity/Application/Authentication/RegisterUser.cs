using FluentValidation;

using Forum.Common.Cqrs;
using Forum.Modules.Identity.Application.Abstractions;
using Forum.Modules.Identity.Application.Validation;
using Forum.Modules.Identity.Contracts.IntegrationEvents;
using Forum.Modules.Identity.Domain.Authorization;
using Forum.Modules.Identity.Domain.Users;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Identity.Application.Authentication;

/// <summary>Registers a new account: unique email + username, Argon2id hash, default <c>user</c> role.</summary>
internal sealed record RegisterUserCommand(string Username, string Email, string DisplayName, string Password)
    : ICommand<RegisterUserResponse>;

internal sealed record RegisterUserResponse(Ulid UserId);

internal sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(static command => command.Username)
            .NotEmpty().Length(3, 32)
            .Matches("^[A-Za-z0-9_.-]+$").WithMessage("Username may only contain letters, digits, '.', '_' and '-'.");
        RuleFor(static command => command.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(static command => command.DisplayName).NotEmpty().MaximumLength(64);
        RuleFor(static command => command.Password).NotEmpty().Length(8, 128);
    }
}

internal sealed class RegisterUserCommandHandler : ICommandHandler<RegisterUserCommand, RegisterUserResponse>
{
    private readonly IValidator<RegisterUserCommand> _validator;
    private readonly IUserRepository _users;
    private readonly IAuthorizationStore _authorization;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public RegisterUserCommandHandler(
        IValidator<RegisterUserCommand> validator,
        IUserRepository users,
        IAuthorizationStore authorization,
        IPasswordHasher passwordHasher,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _validator = validator;
        _users = users;
        _authorization = authorization;
        _passwordHasher = passwordHasher;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<RegisterUserResponse>> Handle(RegisterUserCommand command, CancellationToken cancellationToken)
    {
        if (await _validator.ValidateToErrorAsync(command, cancellationToken) is { } validationError)
        {
            return Result.Failure<RegisterUserResponse>(validationError);
        }

        var email = command.Email.Trim();
        var usernameLc = command.Username.Trim().ToLowerInvariant();

        if (await _users.EmailExistsAsync(email, cancellationToken))
        {
            return Result.Failure<RegisterUserResponse>(UserErrors.EmailTaken);
        }

        if (await _users.UsernameExistsAsync(usernameLc, cancellationToken))
        {
            return Result.Failure<RegisterUserResponse>(UserErrors.UsernameTaken);
        }

        var passwordHash = _passwordHasher.Hash(command.Password);
        var user = User.Register(command.Username, email, command.DisplayName, passwordHash);

        _users.Add(user);
        _outbox.Enqueue(new UserRegisteredIntegrationEvent(
            Ulid.NewUlid(), user.Id, user.Username, user.Email, _clock.GetUtcNow()));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // Grant the baseline role so the account has read/create/comment/like, then warm its permission cache.
        if (await _authorization.GetRoleByNameAsync(RoleNames.User, cancellationToken) is { } userRole)
        {
            await _authorization.AssignRoleAsync(user.Id, userRole.RoleId, cancellationToken);
            await _authorization.RecomputeUserCacheAsync(user.Id, cancellationToken);
        }

        return Result.Success(new RegisterUserResponse(user.Id));
    }
}
