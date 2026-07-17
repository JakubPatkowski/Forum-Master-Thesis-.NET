using FluentValidation;

using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Application.Validation;
using Forum.Modules.Social.Contracts.IntegrationEvents;
using Forum.Modules.Social.Domain.Groups;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Groups;

/// <summary>Renames/redescribes/re-visibilities a group. Order: 404 → manage 403 → 422.</summary>
internal sealed record UpdateGroupCommand(Ulid GroupId, string Name, string Description, string Visibility) : ICommand;

internal sealed class UpdateGroupCommandValidator : AbstractValidator<UpdateGroupCommand>
{
    public UpdateGroupCommandValidator()
    {
        RuleFor(static command => command.Name).NotEmpty().Length(3, Group.MaxNameLength);
        RuleFor(static command => command.Description).MaximumLength(Group.MaxDescriptionLength);
    }
}

internal sealed class UpdateGroupCommandHandler : ICommandHandler<UpdateGroupCommand>
{
    private readonly IValidator<UpdateGroupCommand> _validator;
    private readonly IGroupRepository _groups;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public UpdateGroupCommandHandler(
        IValidator<UpdateGroupCommand> validator,
        IGroupRepository groups,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _validator = validator;
        _groups = groups;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(UpdateGroupCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is null)
        {
            return Result.Failure(SocialErrors.AuthenticationRequired);
        }

        var group = await _groups.GetByIdAsync(command.GroupId, cancellationToken);
        if (group is null)
        {
            return Result.Failure(SocialErrors.GroupNotFound);
        }

        if (!await GroupGuards.MayManageAsync(_currentUser, group, cancellationToken))
        {
            return Result.Failure(SocialErrors.GroupForbidden);
        }

        if (!GroupWire.TryParseVisibility(command.Visibility, out var visibility))
        {
            return Result.Failure(SocialErrors.UnknownVisibility);
        }

        if (await _validator.ValidateToErrorAsync(command, cancellationToken) is { } validationError)
        {
            return Result.Failure(validationError);
        }

        group.Update(command.Name, command.Description, visibility);
        _outbox.Enqueue(new GroupUpdatedIntegrationEvent(Ulid.NewUlid(), group.Id, _clock.GetUtcNow()));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
