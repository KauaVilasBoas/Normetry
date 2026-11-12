using Normetry.BuildingBlocks.Results;

namespace Normetry.BuildingBlocks.Cqrs;

/// <summary>Routes a command or query through the pipeline behaviors to its single handler.</summary>
public interface IDispatcher
{
    Task<Result> SendAsync(ICommand command, CancellationToken cancellationToken);

    Task<Result<TValue>> SendAsync<TValue>(
        ICommand<TValue> command,
        CancellationToken cancellationToken
    );

    Task<Result<TValue>> QueryAsync<TValue>(
        IQuery<TValue> query,
        CancellationToken cancellationToken
    );
}
