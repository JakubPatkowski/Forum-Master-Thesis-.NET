using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Identity.Application.Abstractions;
using Forum.Modules.Identity.Contracts.IntegrationEvents;
using Forum.Modules.Identity.Domain.Users;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Identity.Application.Administration;

/// <summary>Adds a per-object ACL entry for a user (e.g. grants <c>moderate</c> on one category), then recomputes the cache.</summary>
internal sealed record AddAclEntryCommand(
    Ulid TargetUserId, string Scope, Ulid? ScopeId, int AllowBits, int DenyBits) : ICommand;

internal sealed class AddAclEntryCommandHandler : ICommandHandler<AddAclEntryCommand>
{
    private static readonly Error InvalidScope = Error.Validation("acl.invalid_scope", "Scope must be global, category or thread.");
    private static readonly Error ScopeIdRequired = Error.Validation("acl.scope_id_required", "A non-global scope requires a scope id.");

    private static readonly HashSet<string> ValidScopes = new(StringComparer.Ordinal)
    {
        PermissionScopes.Global,
        PermissionScopes.Category,
        PermissionScopes.Thread,
    };

    private readonly IUserRepository _users;
    private readonly IAuthorizationStore _authorization;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public AddAclEntryCommandHandler(
        IUserRepository users, IAuthorizationStore authorization, IOutboxWriter outbox, IUnitOfWork unitOfWork, TimeProvider clock)
    {
        _users = users;
        _authorization = authorization;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(AddAclEntryCommand command, CancellationToken cancellationToken)
    {
        if (!ValidScopes.Contains(command.Scope))
        {
            return Result.Failure(InvalidScope);
        }

        if (command.Scope != PermissionScopes.Global && command.ScopeId is null)
        {
            return Result.Failure(ScopeIdRequired);
        }

        if (await _users.GetByIdAsync(command.TargetUserId, cancellationToken) is null)
        {
            return Result.Failure(UserErrors.NotFound);
        }

        await _authorization.AddUserAclEntryAsync(
            command.TargetUserId,
            new AclEntryInput(command.Scope, command.ScopeId, command.AllowBits, command.DenyBits),
            cancellationToken);

        await _authorization.RecomputeUserCacheAsync(command.TargetUserId, cancellationToken);

        _outbox.Enqueue(new AclEntryChangedIntegrationEvent(
            Ulid.NewUlid(), command.TargetUserId, command.Scope, command.ScopeId, _clock.GetUtcNow()));
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}
