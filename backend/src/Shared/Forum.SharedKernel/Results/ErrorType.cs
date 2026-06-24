namespace Forum.SharedKernel.Results;

/// <summary>Category of a domain/application error; mapped to an HTTP status at the edge.</summary>
public enum ErrorType
{
    Failure = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Unauthorized = 4,
    Forbidden = 5,
}
