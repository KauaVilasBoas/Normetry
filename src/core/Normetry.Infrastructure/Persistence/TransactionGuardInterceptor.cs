using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Normetry.Infrastructure.Persistence;

/// <summary>
/// Rejects any EF Core command that runs outside a transaction. The tenant is set only when a
/// transaction starts, so such a command would run with no tenant: it would quietly see no rows,
/// and the missing transaction would go unnoticed.
/// </summary>
internal sealed class TransactionGuardInterceptor : DbCommandInterceptor
{
    public static readonly TransactionGuardInterceptor Instance = new();

    private TransactionGuardInterceptor() { }

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result
    ) => Guard(command, result);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    ) => ValueTask.FromResult(Guard(command, result));

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result
    ) => Guard(command, result);

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    ) => ValueTask.FromResult(Guard(command, result));

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result
    ) => Guard(command, result);

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default
    ) => ValueTask.FromResult(Guard(command, result));

    private static T Guard<T>(DbCommand command, T result) =>
        command.Transaction is not null
            ? result
            : throw new InvalidOperationException(
                "A database command ran outside a transaction, so no tenant was set. Requests get "
                    + "one from the transaction behavior; other code must begin one first."
            );
}
