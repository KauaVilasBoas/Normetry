using Npgsql;
using Xunit;

namespace Normetry.IntegrationTests.Database;

/// <summary>
/// One connection as a given role with one open transaction, and the tenant set the way the
/// interceptor sets it (or none). Disposing without <see cref="CommitAsync"/> rolls back.
/// </summary>
public sealed class Session : IAsyncDisposable
{
    /// <summary>SQLSTATE 42501: a privilege is missing, or a row violates a row-level security policy.</summary>
    public const string Denied = "42501";

    private readonly NpgsqlConnection connection;
    private readonly NpgsqlTransaction transaction;

    private Session(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        this.connection = connection;
        this.transaction = transaction;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static async Task<Session> BeginAsync(string connectionString, Guid? tenantId = null)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Ct);
        var session = new Session(connection, await connection.BeginTransactionAsync(Ct));
        if (tenantId is { } tenant)
        {
            await session.ExecuteAsync(
                "SELECT set_config('app.tenant_id', $1, true)",
                tenant.ToString()
            );
        }
        return session;
    }

    /// <summary>Runs a statement with positional parameters ($1, $2, ...) and returns the rows it affected.</summary>
    public async Task<int> ExecuteAsync(string sql, params object?[] parameters)
    {
        await using var command = Command(sql, parameters);
        return await command.ExecuteNonQueryAsync(Ct);
    }

    public async Task<T> ScalarAsync<T>(string sql, params object?[] parameters)
    {
        await using var command = Command(sql, parameters);
        return (T)(await command.ExecuteScalarAsync(Ct))!;
    }

    /// <summary>The first column of every row.</summary>
    public async Task<List<T>> ColumnAsync<T>(string sql, params object?[] parameters)
    {
        await using var command = Command(sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(Ct);
        var values = new List<T>();
        while (await reader.ReadAsync(Ct))
        {
            values.Add(reader.GetFieldValue<T>(0));
        }
        return values;
    }

    /// <summary>The statement must fail with a privilege or row-level security error.</summary>
    public async Task AssertDeniedAsync(string sql, params object?[] parameters)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync(sql, parameters)
        );
        Assert.Equal(Denied, error.SqlState);
    }

    public Task CommitAsync() => transaction.CommitAsync(Ct);

    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
    }

    private NpgsqlCommand Command(string sql, object?[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(new NpgsqlParameter { Value = parameter ?? DBNull.Value });
        }
        return command;
    }
}
