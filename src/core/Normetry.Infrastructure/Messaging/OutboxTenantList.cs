using Npgsql;

namespace Normetry.Infrastructure.Messaging;

/// <summary>
/// The tenants the background services visit (<c>core.outbox_tenants</c>): every tenant that has
/// written an outbox message, which covers every tenant with a document. This is the one query
/// without a tenant, so it runs as <c>core_dispatcher</c>
/// (<see cref="CoreDatabase.DispatcherConnectionStringName"/>), a role that reads these ids and
/// nothing else; <c>app_core</c> cannot read them. Every row of tenant data is read afterwards as
/// <c>app_core</c>, in that tenant's own transaction, under RLS.
/// </summary>
internal static class OutboxTenantList
{
    public static async Task<List<Guid>> ListAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT tenant_id FROM core.outbox_tenants",
            connection
        );
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var tenants = new List<Guid>();
        while (await reader.ReadAsync(cancellationToken))
        {
            tenants.Add(reader.GetGuid(0));
        }
        return tenants;
    }
}
