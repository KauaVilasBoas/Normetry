using FluentValidation;
using FluentValidation.Results;
using Normetry.BuildingBlocks.Results;

namespace Normetry.BuildingBlocks.Cqrs;

/// <summary>
/// Runs every <see cref="IValidator{T}"/> for the request. On failure it returns a validation
/// <see cref="Error"/> and the handler is never called. Every request returns a
/// <see cref="Result"/> or <see cref="Result{TValue}"/>, so the constraint always holds.
/// </summary>
internal sealed class ValidationBehavior<TRequest, TResult>(
    IEnumerable<IValidator<TRequest>> validators
) : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
    where TResult : IFailureFactory<TResult>
{
    public async Task<TResult> HandleAsync(
        TRequest request,
        Func<Task<TResult>> next,
        CancellationToken cancellationToken
    )
    {
        var failures = new List<ValidationFailure>();
        foreach (var validator in validators)
        {
            var result = await validator.ValidateAsync(request, cancellationToken);
            failures.AddRange(result.Errors);
        }

        if (failures.Count == 0)
        {
            return await next();
        }

        return TResult.Failure(
            new Error("validation", "One or more validation errors occurred.", ErrorType.Validation)
            {
                FieldErrors = failures
                    .GroupBy(failure => failure.PropertyName)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(failure => failure.ErrorMessage).ToArray()
                    ),
            }
        );
    }
}
