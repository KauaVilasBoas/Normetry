using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Normetry.Application.Abstractions;

namespace Normetry.Infrastructure.Persistence;

/// <summary>
/// Sets <c>app.tenant_id</c> as the first statement of every transaction EF Core starts or
/// adopts. <c>set_config(..., true)</c> is transaction-local, so a pooled connection never
/// carries the tenant into the next request (ADR 003). Handlers never set it themselves. A
/// transaction that already holds another tenant is rejected instead of being switched over.
/// </summary>
internal sealed class TenantTransactionInterceptor(ITenantContext tenant) : DbTransactionInterceptor
{
    public override DbTransaction TransactionStarted(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result
    )
    {
        SetTenant(connection, result);
        return result;
    }

    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default
    )
    {
        await SetTenantAsync(connection, result, cancellationToken);
        return result;
    }

    // A transaction started outside EF Core (Database.UseTransaction) gets the tenant too.
    public override DbTransaction TransactionUsed(
        DbConnection connection,
        TransactionEventData eventData,
        DbTransaction result
    )
    {
        SetTenant(connection, result);
        return result;
    }

    public override async ValueTask<DbTransaction> TransactionUsedAsync(
        DbConnection connection,
        TransactionEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default
    )
    {
        await SetTenantAsync(connection, result, cancellationToken);
        return result;
    }

    private void SetTenant(DbConnection connection, DbTransaction transaction)
    {
        using var command = SetTenantCommand(connection, transaction);
        EnsureSet(command.ExecuteScalar());
    }

    private async Task SetTenantAsync(
        DbConnection connection,
        DbTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        await using var command = SetTenantCommand(connection, transaction);
        EnsureSet(await command.ExecuteScalarAsync(cancellationToken));
    }

    private DbCommand SetTenantCommand(DbConnection connection, DbTransaction transaction)
    {
        // Throws when the request has no valid tenant, so the transaction never runs a query.
        var tenantId = tenant.TenantId;
        if (tenantId == Guid.Empty)
        {
            throw new InvalidOperationException("The tenant id must not be the empty UUID.");
        }

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        // No row when the transaction (or the session) already holds a different tenant.
        command.CommandText = """
            SELECT set_config('app.tenant_id', @tenant, true)
            WHERE coalesce(current_setting('app.tenant_id', true), '') IN ('', @tenant)
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant";
        parameter.Value = tenantId.ToString();
        command.Parameters.Add(parameter);
        return command;
    }

    private static void EnsureSet(object? result)
    {
        if (result is null or DBNull)
        {
            throw new InvalidOperationException(
                "The transaction is already scoped to another tenant."
            );
        }
    }
}
