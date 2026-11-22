namespace Normetry.BuildingBlocks.Results;

/// <summary>Kind of expected failure; the API maps each one to an HTTP status code.</summary>
public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,

    /// <summary>A limit such as a daily quota is reached; the caller may retry later.</summary>
    RateLimited,
    Unexpected,
}

/// <summary>An expected failure. Exceptions stay for failures nobody planned for.</summary>
public sealed record Error(string Code, string Message, ErrorType Type)
{
    /// <summary>The code of every input failure; its field errors say what is wrong.</summary>
    public const string ValidationCode = "validation";

    /// <summary>Messages per input field, set by <see cref="Validation"/>.</summary>
    public IReadOnlyDictionary<string, string[]>? FieldErrors { get; init; }

    /// <summary>An input failure with its messages per field.</summary>
    public static Error Validation(IReadOnlyDictionary<string, string[]> fieldErrors) =>
        new(ValidationCode, "One or more validation errors occurred.", ErrorType.Validation)
        {
            FieldErrors = fieldErrors,
        };
}
