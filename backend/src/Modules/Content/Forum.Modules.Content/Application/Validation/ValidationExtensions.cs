using FluentValidation;
using FluentValidation.Results;

using Forum.SharedKernel.Results;

namespace Forum.Modules.Content.Application.Validation;

/// <summary>Bridges FluentValidation into the Result pattern — a failed validation becomes a single 422 error.</summary>
internal static class ValidationExtensions
{
    /// <summary>Validates and returns null on success, or a <see cref="ErrorType.Validation"/> error describing the failures.</summary>
    public static async Task<Error?> ValidateToErrorAsync<T>(
        this IValidator<T> validator, T instance, CancellationToken cancellationToken)
    {
        ValidationResult result = await validator.ValidateAsync(instance, cancellationToken);
        if (result.IsValid)
        {
            return null;
        }

        var description = string.Join("; ", result.Errors.Select(static failure => failure.ErrorMessage));
        return Error.Validation("validation.failed", description);
    }
}
