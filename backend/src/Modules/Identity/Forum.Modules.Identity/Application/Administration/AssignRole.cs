using Forum.Common.Cqrs;
using Forum.Modules.Identity.Application.Abstractions;
using Forum.Modules.Identity.Contracts.IntegrationEvents;
using Forum.Modules.Identity.Domain.Users;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Identity.Application.Administration;

/// <summary>Assigns or revokes a global role for a user, then recomputes their permission cache.</summary>
internal sealed record AssignRoleCommand(Ulid TargetUserId, string RoleName, bool Assign) : ICommand;

internal sealed class AssignRoleCommandHandler : ICommandHandler<AssignRoleCommand>
{
    private static readonly Error UnknownRole = Error.Validation("role.unknown", "Unknown role.");

    private readonly IUserRepository _users;
    private readonly IAuthorizationStore _authorization;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public AssignRoleCommandHandler(
        IUserRepository users, IAuthorizationStore authorization, IOutboxWriter outbox, IUnitOfWork unitOfWork, TimeProvider clock)
    {
        _users = users;
        _authorization = authorization;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(AssignRoleCommand command, CancellationToken cancellationToken)
    {
        if (await _users.GetByIdAsync(command.TargetUserId, cancellationToken) is null)
        {
            return Result.Failure(UserErrors.NotFound);
        }

        var role = await _authorization.GetRoleByNameAsync(command.RoleName.Trim().ToLowerInvariant(), cancellationToken);
        if (role is null)
        {
            return Result.Failure(UnknownRole);
        }

        if (command.Assign)
        {
            await _authorization.AssignRoleAsync(command.TargetUserId, role.RoleId, cancellationToken);
        }
        else
        {
            await _authorization.RevokeRoleAsync(command.TargetUserId, role.RoleId, cancellationToken);
        }

        await _authorization.RecomputeUserCacheAsync(command.TargetUserId, cancellationToken);

        _outbox.Enqueue(new RoleAssignedIntegrationEvent(
            Ulid.NewUlid(), command.TargetUserId, role.RoleId, _clock.GetUtcNow()));
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
