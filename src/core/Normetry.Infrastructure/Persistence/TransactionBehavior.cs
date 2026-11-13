using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Infrastructure.Persistence;

/// <summary>
/// Runs every request, queries included, in one transaction, which the interceptor scopes to the
/// request's tenant. A successful command saves its changes and commits; a failed result or an
/// exception rolls back, and a query never commits.
/// </summary>
internal sealed class TransactionBehavior<TRequest, TResult>(CoreDbContext db)
    : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
    where TResult : Result
{
    private static readonly bool IsQuery = typeof(TRequest)
        .GetInterfaces()
        .Any(contract =>
            contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IQuery<>)
        );

    public async Task<TResult> HandleAsync(
        TRequest request,
        Func<Task<TResult>> next,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var result = await next();
        if (result.IsSuccess && !IsQuery)
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        return result;
    }
}
