using FluentValidation;

using Forum.Common.Cqrs;
using Forum.Common.Security;
using Forum.Modules.Content.Application.Abstractions;
using Forum.Modules.Content.Application.Validation;
using Forum.Modules.Content.Contracts.IntegrationEvents;
using Forum.Modules.Content.Domain.Categories;
using Forum.Modules.Content.Domain.Threads;
using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Threads;

/// <summary>
/// Creates a thread in a category, attaching tags (created on first use). Order: category 404 → permission
/// 403 (<c>create</c> resolved at the category scope, so per-category denies apply) → input 422.
/// </summary>
internal sealed record CreateThreadCommand(Ulid CategoryId, string Title, string Body, IReadOnlyList<string> TagSlugs)
    : ICommand<CreateThreadResponse>;

internal sealed record CreateThreadResponse(Ulid ThreadId);

internal sealed class CreateThreadCommandValidator : AbstractValidator<CreateThreadCommand>
{
    public CreateThreadCommandValidator()
    {
        RuleFor(static command => command.Title).NotEmpty().Length(3, 200);
        RuleFor(static command => command.Body).NotEmpty().MaximumLength(50_000);
        RuleFor(static command => command.TagSlugs)
            .Must(static tags => tags.Count <= 5).WithMessage("A thread may carry at most 5 tags.");
        RuleForEach(static command => command.TagSlugs)
            .NotEmpty().MaximumLength(32)
            .Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$")
            .WithMessage("Tags may only contain lower-case letters, digits and single dashes.");
    }
}

internal sealed class CreateThreadCommandHandler : ICommandHandler<CreateThreadCommand, CreateThreadResponse>
{
    private readonly IValidator<CreateThreadCommand> _validator;
    private readonly ICategoryRepository _categories;
    private readonly IThreadRepository _threads;
    private readonly ITagRepository _tags;
    private readonly ICurrentUser _currentUser;
    private readonly IOutboxWriter _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _clock;

    public CreateThreadCommandHandler(
        IValidator<CreateThreadCommand> validator,
        ICategoryRepository categories,
        IThreadRepository threads,
        ITagRepository tags,
        ICurrentUser currentUser,
        IOutboxWriter outbox,
        IUnitOfWork unitOfWork,
        TimeProvider clock)
    {
        _validator = validator;
        _categories = categories;
        _threads = threads;
        _tags = tags;
        _currentUser = currentUser;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<CreateThreadResponse>> Handle(CreateThreadCommand command, CancellationToken cancellationToken)
    {
        if (_currentUser.Id is not { } ownerId)
        {
            return Result.Failure<CreateThreadResponse>(ContentErrors.AuthenticationRequired);
        }

        var category = await _categories.GetByIdAsync(command.CategoryId, cancellationToken);
        if (category is null)
        {
            return Result.Failure<CreateThreadResponse>(CategoryErrors.NotFound);
        }

        // A private category accepts threads only from its owner or a moderator of that category.
        if (category.Visibility == Visibility.Private
            && !await _currentUser.IsOwnerOrModeratorAsync(category.OwnerId, category.Id, cancellationToken))
        {
            return Result.Failure<CreateThreadResponse>(CategoryErrors.PrivateCategory);
        }

        if (!await _currentUser.HasPermissionAsync(
                Permissions.Create, PermissionScopes.Category, category.Id, cancellationToken))
        {
            return Result.Failure<CreateThreadResponse>(ThreadErrors.CreateForbidden);
        }

        if (await _validator.ValidateToErrorAsync(command, cancellationToken) is { } validationError)
        {
            return Result.Failure<CreateThreadResponse>(validationError);
        }

        var thread = Thread.Create(category.Id, ownerId, command.Title, command.Body);
        _threads.Add(thread);

        var slugs = command.TagSlugs.Select(static slug => slug.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        if (slugs.Length > 0)
        {
            var existing = await _tags.GetBySlugsAsync(slugs, cancellationToken);
            var tagIds = new List<Ulid>(slugs.Length);
            foreach (var slug in slugs)
            {
                var tag = existing.FirstOrDefault(tag => tag.Slug == slug);
                if (tag is null)
                {
                    tag = Tag.Create(slug, slug);
                    _tags.Add(tag);
                }

                tagIds.Add(tag.Id);
            }

            _threads.AttachTags(thread.Id, tagIds);
        }

        _outbox.Enqueue(new ThreadCreatedIntegrationEvent(
            Ulid.NewUlid(), thread.Id, category.Id, ownerId, thread.Title, _clock.GetUtcNow()));

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new CreateThreadResponse(thread.Id));
    }
}
