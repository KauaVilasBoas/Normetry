using Normetry.BuildingBlocks.Results;

namespace Normetry.BuildingBlocks.Cqrs;

/// <summary>
/// Anything the dispatcher can route. <typeparamref name="TResult"/> is the full response type,
/// always a <see cref="Result"/> or <see cref="Result{TValue}"/> for the requests below.
/// </summary>
public interface IRequest<TResult>;

/// <summary>Changes state and returns no value.</summary>
public interface ICommand : IRequest<Result>;

/// <summary>Changes state and returns a small value, such as the id of what it created.</summary>
public interface ICommand<TValue> : IRequest<Result<TValue>>;

/// <summary>Reads state without changing it.</summary>
public interface IQuery<TValue> : IRequest<Result<TValue>>;
