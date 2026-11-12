using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Normetry.Infrastructure.Persistence;

/// <summary>
/// Stops the API from starting on a role that can get around RLS: a superuser, a BYPASSRLS role,
/// or the owner of schema <c>core</c> or of anything in it (directly or by membership). Any of
/// them would read every tenant or could switch RLS off. The AI service runs the same check on
/// schema <c>ai</c>.
/// </summary>
public sealed class RuntimeRoleCheck(IConfiguration configuration) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) =>
        EnsureAsync(CoreDatabase.GetConnectionString(configuration), cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Throws <see cref="InvalidOperationException"/> naming every problem found.</summary>
    public static async Task EnsureAsync(
        string connectionString,
        CancellationToken cancellationToken
    )
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT current_user::text, rolsuper, rolbypassrls,
                   EXISTS (SELECT FROM pg_namespace
                           WHERE nspname = 'core' AND pg_has_role(nspowner, 'MEMBER'))
                   OR EXISTS (SELECT FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                              WHERE n.nspname = 'core' AND pg_has_role(c.relowner, 'MEMBER'))
            FROM pg_roles WHERE rolname = current_user
            """,
            connection
        );
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var user = reader.GetString(0);
        string[] problems =
        [
            .. new (string Reason, bool Found)[]
            {
                ("is a superuser", reader.GetBoolean(1)),
                ("has BYPASSRLS", reader.GetBoolean(2)),
                ("owns schema core or objects in it", reader.GetBoolean(3)),
            }
                .Where(check => check.Found)
                .Select(check => check.Reason),
        ];
        if (problems.Length > 0)
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{CoreDatabase.ConnectionStringName} connects as '{user}', which "
                    + $"{string.Join(" and ", problems)}. The API must connect as app_core."
            );
        }
    }
}
