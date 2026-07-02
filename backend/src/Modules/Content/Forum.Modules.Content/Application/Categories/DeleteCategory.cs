using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Domain.Categories;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Categories;

/// <summary>Soft-deletes a category. Allowed for the owner or a moderator.</summary>
internal sealed record DeleteCategoryCommand(string Slug) : ICommand;

internal sealed class DeleteCategoryCommandHandler : ICommandHandler<DeleteCategoryCommand>
{
    private readonly ICategoryRepository _categories;
    private readonly ICurrentUser _currentUser;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public DeleteCategoryCommandHandler(
        ICategoryRepository categories, ICurrentUser currentUser, IUnitOfWork unitOfWork, TimeProvider clock)
    {
        _categories = categories;
        _currentUser = currentUser;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> Handle(DeleteCategoryCommand command, CancellationToken cancellationToken)
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

        var result = category.Delete(_currentUser.Id ?? Ulid.Empty, _clock.GetUtcNow());
        if (result.IsFailure)
        {
            return result;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
