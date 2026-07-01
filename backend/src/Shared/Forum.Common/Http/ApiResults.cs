using Forum.SharedKernel.Results;

using Microsoft.AspNetCore.Http;

namespace Forum.Common.Http;

/// <summary>
/// Maps the <see cref="Result"/> pattern to HTTP at the REST edge. A failed result becomes an RFC 7807
/// ProblemDetails carrying the <c>{ code, description, type }</c> envelope; the status follows <see cref="ErrorType"/>.
/// </summary>
public static class ApiResults
{
    /// <summary>Maps a unit result: <paramref name="onSuccess"/> on success, a problem response on failure.</summary>
    public static IResult Match(this Result result, Func<IResult> onSuccess) =>
        result.IsSuccess ? onSuccess() : Problem(result.Error);

    /// <summary>Maps a value result: <paramref name="onSuccess"/> on success, a problem response on failure.</summary>
    public static IResult Match<TValue>(this Result<TValue> result, Func<TValue, IResult> onSuccess) =>
        result.IsSuccess ? onSuccess(result.Value) : Problem(result.Error);

    /// <summary>Translates a typed <see cref="Error"/> into a ProblemDetails response.</summary>
    public static IResult Problem(Error error) => Results.Problem(
        statusCode: StatusFor(error.Type),
        title: error.Description,
        extensions: new Dictionary<string, object?>
        {
            ["code"] = error.Code,
            ["errorType"] = error.Type.ToString(),
        });

    private static int StatusFor(ErrorType type) => type switch
    {
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Validation => StatusCodes.Status422UnprocessableEntity,
        _ => StatusCodes.Status400BadRequest,
    };
}
