using FluentValidation;

using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Application.Caching;
using Forum.Modules.Content.Application.Validation;
using Forum.Modules.Content.Domain.Categories;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Categories;

/// <summary>Updates a category's details. Allowed for the owner or a moderator (checked in the 404 → 403 → 422 order).</summary>
internal sealed record UpdateCategoryCommand(string Slug, string Name, string Description, Visibility Visibility)
    : ICommand;

internal sealed class UpdateCategoryCommandValidator : AbstractValidator<UpdateCategoryCommand>
{
    public UpdateCategoryCommandValidator()
    {
        RuleFor(static command => command.Name).NotEmpty().Length(3, 128);
        RuleFor(static command => command.Description).MaximumLength(2000);
        RuleFor(static command => command.Visibility).IsInEnum();
    }
}

internal sealed class UpdateCategoryCommandHandler : ICommandHandler<UpdateCategoryCommand>
{
    private readonly IValidator<UpdateCategoryCommand> _validator;
    private readonly ICategoryRepository _categories;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IContentReadCache _readCache;

    public UpdateCategoryCommandHandler(
        IValidator<UpdateCategoryCommand> validator,
        ICategoryRepository categories,
        ICurrentUser currentUser,
        IUnitOfWork unitOfWork,
        IContentReadCache readCache)
    {
        _validator = validator;
        _categories = categories;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
        _readCache = readCache;
    }

    public async Task<Result> Handle(UpdateCategoryCommand command, CancellationToken cancellationToken)
    {
        var category = await _categories.GetBySlugAsync(command.Slug, cancellationToken);
        if (category is null)
        {
            return Result.Failure(CategoryErrors.NotFound);
        }

        if (!await _currentUser.IsOwnerOrModeratorAsync(category.OwnerId, category.Id, cancellationToken))
        {
            return Result.Failure(CategoryErrors.NotOwnerNorModerator);
        }

        if (await _validator.ValidateToErrorAsync(command, cancellationToken) is { } validationError)
        {
            return Result.Failure(validationError);
        }

        category.UpdateDetails(command.Name, command.Description);
        category.ChangeVisibility(command.Visibility);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        await _readCache.CategoryChangedAsync(); // READ-CACHE-CONTRACT §3: C1 all, C2 all, C3 all
        return Result.Success();
    }
}
