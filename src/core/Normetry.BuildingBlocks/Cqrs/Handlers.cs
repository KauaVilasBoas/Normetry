using Normetry.BuildingBlocks.Results;

namespace Normetry.BuildingBlocks.Cqrs;

/// <summary>
/// Common shape of every handler. Implement one of the interfaces below instead: they pin the
/// result type. <see cref="DispatcherServiceCollectionExtensions.AddDispatcher"/> registers
/// handlers under this interface, so the dispatcher needs a single code path.
/// </summary>
public interface IRequestHandler<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    Task<TResult> HandleAsync(TRequest request, CancellationToken cancellationToken);
}

public interface ICommandHandler<TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;

public interface ICommandHandler<TCommand, TValue> : IRequestHandler<TCommand, Result<TValue>>
    where TCommand : ICommand<TValue>;

public interface IQueryHandler<TQuery, TValue> : IRequestHandler<TQuery, Result<TValue>>
    where TQuery : IQuery<TValue>;
