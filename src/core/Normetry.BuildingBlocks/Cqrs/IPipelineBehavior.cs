namespace Normetry.BuildingBlocks.Cqrs;

/// <summary>
/// Code that wraps every handler call, such as logging, validation or a transaction. Behaviors run
/// in registration order. Calling <paramref name="next"/> runs the rest of the pipeline; returning
/// without calling it short-circuits the handler.
/// </summary>
public interface IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    Task<TResult> HandleAsync(
        TRequest request,
        Func<Task<TResult>> next,
        CancellationToken cancellationToken
    );
}
