using FluentValidation;

using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Application.Validation;
using Forum.Modules.Content.Contracts.IntegrationEvents;
using Forum.Modules.Content.Domain.Categories;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Categories;

/// <summary>Creates a category owned by the current user. Slug is the unique URL identifier.</summary>
internal sealed record CreateCategoryCommand(string Slug, string Name, string Description, Visibility Visibility)
    : ICommand<CreateCategoryResponse>;

internal sealed record CreateCategoryResponse(Ulid CategoryId, string Slug);

internal sealed class CreateCategoryCommandValidator : AbstractValidator<CreateCategoryCommand>
{
    public CreateCategoryCommandValidator()
    {
        RuleFor(static command => command.Slug)
            .NotEmpty().Length(3, 64)
            .Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$")
            .WithMessage("Slug may only contain lower-case letters, digits and single dashes.");
        RuleFor(static command => command.Name).NotEmpty().Length(3, 128);
        RuleFor(static command => command.Description).MaximumLength(2000);
        RuleFor(static command => command.Visibility).IsInEnum();
    }
}

internal sealed class CreateCategoryCommandHandler : ICommandHandler<CreateCategoryCommand, CreateCategoryResponse>
{
    private readonly IValidator<CreateCategoryCommand> _validator;
    private readonly ICategoryRepository _categories;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public CreateCategoryCommandHandler(
        IValidator<CreateCategoryCommand> validator,
        ICategoryRepository categories,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _validator = validator;
        _categories = categories;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<CreateCategoryResponse>> Handle(CreateCategoryCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } ownerId)
        {
            return Result.Failure<CreateCategoryResponse>(ContentErrors.AuthenticationRequired);
        }

        if (await _validator.ValidateToErrorAsync(command, cancellationToken) is { } validationError)
        {
            return Result.Failure<CreateCategoryResponse>(validationError);
        }

        if (await _categories.SlugExistsAsync(command.Slug, cancellationToken))
        {
            return Result.Failure<CreateCategoryResponse>(CategoryErrors.SlugTaken);
        }

        var category = Category.Create(command.Slug, command.Name, command.Description, command.Visibility, ownerId);

        _categories.Add(category);
        _outbox.Enqueue(new CategoryCreatedIntegrationEvent(
            Ulid.NewUlid(), category.Id, category.Slug, ownerId, _clock.GetUtcNow()));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateCategoryResponse(category.Id, category.Slug));
    }
}
