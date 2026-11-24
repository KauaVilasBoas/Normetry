using Microsoft.EntityFrameworkCore;
using Normetry.Application.Identity;
using Normetry.Application.Inspections;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.Infrastructure.Persistence.Configurations;
using Npgsql;

namespace Normetry.Infrastructure.Persistence;

/// <summary>
/// Runs every request, queries included, in one transaction, which the interceptor scopes to the
/// request's tenant. A successful command saves its changes and commits; a failed result or an
/// exception rolls back, and a query never commits. A unique index that refuses the save in a
/// race the handler's own check cannot see (two concurrent requests) becomes that index's
/// refusal instead of a 500. After the commit it runs the scope's <see cref="AfterCommit"/> work.
/// </summary>
internal sealed class TransactionBehavior<TRequest, TResult>(
    CoreDbContext db,
    AfterCommit afterCommit
) : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
    where TResult : Result, IFailureFactory<TResult>
{
    // Unique indexes whose violation is an expected refusal, by name.
    private static readonly Dictionary<string, Error> UniqueRefusals = new()
    {
        [ProfileConfiguration.NameIndex] = IdentityErrors.NameTaken,
        [InspectionConfiguration.InFlightIndex] = InspectionErrors.Running,
    };

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
            try
            {
                await db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException error)
                when (error.InnerException
                        is PostgresException
                        {
                            SqlState: PostgresErrorCodes.UniqueViolation,
                            ConstraintName: { } constraint,
                        }
                    && UniqueRefusals.TryGetValue(constraint, out var refusal)
                )
            {
                // The transaction is aborted; disposing it rolls back.
                return TResult.Failure(refusal);
            }
            await transaction.CommitAsync(cancellationToken);
            afterCommit.Run();
        }
        return result;
    }
}
