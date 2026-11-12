using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Api;

public static class ResultProblemDetails
{
    /// <summary>
    /// Maps a failed result to an RFC 9457 problem response. The error code goes in the
    /// <c>code</c> extension. A 500 never carries the error message, so internals cannot leak.
    /// </summary>
    public static ProblemHttpResult ToProblemDetails(this Result result)
    {
        var error = result.Error;
        var problem = error.FieldErrors is { } fieldErrors
            ? new HttpValidationProblemDetails(fieldErrors)
            : new ProblemDetails();

        problem.Status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status500InternalServerError,
        };
        problem.Detail = error.Type == ErrorType.Unexpected ? null : error.Message;
        problem.Extensions["code"] = error.Code;

        return TypedResults.Problem(problem);
    }
}
