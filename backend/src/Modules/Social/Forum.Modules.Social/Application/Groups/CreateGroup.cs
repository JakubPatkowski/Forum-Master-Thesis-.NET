using FluentValidation;

using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Social.Application.Abstractions;
using Forum.Modules.Social.Application.Validation;
using Forum.Modules.Social.Contracts.IntegrationEvents;
using Forum.Modules.Social.Domain.Conversations;
using Forum.Modules.Social.Domain.Groups;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Social.Application.Groups;

/// <summary>
/// Creates a group plus, in the same transaction, its chat conversation (SAME ULID as the group — no FK
/// indirection), the owner's membership row and the owner's conversation seat. Any authenticated user may create
/// groups; admin-level rights inside it come from ownership, not an ACL grant.
/// </summary>
internal sealed record CreateGroupCommand(string Name, string Description, string Visibility)
    : ICommand<CreateGroupResponse>;

internal sealed record CreateGroupResponse(Ulid GroupId);

internal sealed class CreateGroupCommandValidator : AbstractValidator<CreateGroupCommand>
{
    public CreateGroupCommandValidator()
    {
        RuleFor(static command => command.Name).NotEmpty().Length(3, Group.MaxNameLength);
        RuleFor(static command => command.Description).MaximumLength(Group.MaxDescriptionLength);
    }
}

internal sealed class CreateGroupCommandHandler : ICommandHandler<CreateGroupCommand, CreateGroupResponse>
{
    private readonly IValidator<CreateGroupCommand> _validator;
    private readonly IGroupRepository _groups;
    private readonly IConversationRepository _conversations;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public CreateGroupCommandHandler(
        IValidator<CreateGroupCommand> validator,
        IGroupRepository groups,
        IConversationRepository conversations,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _validator = validator;
        _groups = groups;
        _conversations = conversations;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<CreateGroupResponse>> Handle(
        CreateGroupCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } userId)
        {
            return Result.Failure<CreateGroupResponse>(SocialErrors.AuthenticationRequired);
        }

        if (!GroupWire.TryParseVisibility(command.Visibility, out var visibility))
        {
            return Result.Failure<CreateGroupResponse>(SocialErrors.UnknownVisibility);
        }

        if (await _validator.ValidateToErrorAsync(command, cancellationToken) is { } validationError)
        {
            return Result.Failure<CreateGroupResponse>(validationError);
        }

        var now = _clock.GetUtcNow();
        var group = Group.Create(command.Name, command.Description, visibility, userId);
        _groups.Add(group);
        _groups.AddMembership(new GroupMembership(group.Id, userId, now, invitedBy: null));
        _conversations.Add(Conversation.CreateForGroup(group.Id));
        _conversations.AddParticipant(new ConversationParticipant(group.Id, userId, now));

        _outbox.Enqueue(new GroupCreatedIntegrationEvent(Ulid.NewUlid(), group.Id, userId, now));

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(new CreateGroupResponse(group.Id));
    }
}
