using Microsoft.AspNetCore.Http;
using Normetry.Api;
using Normetry.BuildingBlocks.Results;
using Xunit;

namespace Normetry.UnitTests.Api;

public sealed class ResultProblemDetailsTests
{
    [Theory]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.RateLimited, 429)]
    [InlineData(ErrorType.Unexpected, 500)]
    public void Each_error_type_maps_to_its_status_code(ErrorType type, int status)
    {
        var problem = Result
            .Failure(new Error("establishment.not_found", "Not found.", type))
            .ToProblemDetails();

        Assert.Equal(status, problem.StatusCode);
        Assert.Equal("establishment.not_found", problem.ProblemDetails.Extensions["code"]);
    }

    [Fact]
    public void Field_errors_become_validation_problem_details()
    {
        var error = new Error("validation", "Invalid.", ErrorType.Validation)
        {
            FieldErrors = new Dictionary<string, string[]> { ["Name"] = ["Required."] },
        };

        var problem = Result<int>.Failure(error).ToProblemDetails();

        var details = Assert.IsType<HttpValidationProblemDetails>(problem.ProblemDetails);
        Assert.Equal(["Required."], details.Errors["Name"]);
    }

    [Fact]
    public void An_unexpected_error_does_not_expose_its_message()
    {
        var problem = Result
            .Failure(new Error("db", "Connection string Host=...", ErrorType.Unexpected))
            .ToProblemDetails();

        Assert.Null(problem.ProblemDetails.Detail);
    }
}
