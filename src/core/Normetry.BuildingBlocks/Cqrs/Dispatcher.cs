using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Normetry.BuildingBlocks.Results;

namespace Normetry.BuildingBlocks.Cqrs;

internal sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    /// <summary>
    /// One wrapper per request type, built by reflection on the first dispatch and reused after
    /// that. Internal so tests can check that a second dispatch does not rebuild it.
    /// </summary>
    internal static readonly ConcurrentDictionary<Type, object> Wrappers = new();

    public Task<Result> SendAsync(ICommand command, CancellationToken cancellationToken) =>
        DispatchAsync(command, cancellationToken);

    public Task<Result<TValue>> SendAsync<TValue>(
        ICommand<TValue> command,
        CancellationToken cancellationToken
    ) => DispatchAsync(command, cancellationToken);

    public Task<Result<TValue>> QueryAsync<TValue>(
        IQuery<TValue> query,
        CancellationToken cancellationToken
    ) => DispatchAsync(query, cancellationToken);

    private Task<TResult> DispatchAsync<TResult>(
        IRequest<TResult> request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        // The caller only knows the request as an interface; the wrapper is the one place that
        // knows its concrete type, so it can resolve the typed handler and behaviors.
        var wrapper =
            (RequestHandlerWrapper<TResult>)
                Wrappers.GetOrAdd(request.GetType(), static requestType =>
                    Activator.CreateInstance(
                        typeof(RequestHandlerWrapper<,>).MakeGenericType(
                            requestType,
                            typeof(TResult)
                        )
                    )!
                );

        return wrapper.HandleAsync(request, services, cancellationToken);
    }
}

internal abstract class RequestHandlerWrapper<TResult>
{
    public abstract Task<TResult> HandleAsync(
        IRequest<TResult> request,
        IServiceProvider services,
        CancellationToken cancellationToken
    );
}

internal sealed class RequestHandlerWrapper<TRequest, TResult> : RequestHandlerWrapper<TResult>
    where TRequest : IRequest<TResult>
{
    public override Task<TResult> HandleAsync(
        IRequest<TResult> request,
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        var typedRequest = (TRequest)request;
        var handler = services.GetService<IRequestHandler<TRequest, TResult>>()
            ?? throw new InvalidOperationException(
                $"No handler is registered for request type '{typeof(TRequest).FullName}'."
            );

        Func<Task<TResult>> next = () => handler.HandleAsync(typedRequest, cancellationToken);

        // Wrap from the last behavior inwards, so the first registered behavior runs first.
        foreach (
            var behavior in services.GetServices<IPipelineBehavior<TRequest, TResult>>().Reverse()
        )
        {
            var inner = next;
            next = () => behavior.HandleAsync(typedRequest, inner, cancellationToken);
        }

        return next();
    }
}
