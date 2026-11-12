using Microsoft.EntityFrameworkCore;
using Normetry.Infrastructure.Persistence;

namespace Normetry.Migrations;

public static class CoreMigrator
{
    /// <summary>The only setting: the <c>core_owner</c> connection string.</summary>
    public const string ConnectionStringVariable = "ConnectionStrings__core-migrations";

    /// <summary>
    /// Applies every pending migration in <c>Schema/</c> and <c>Data/</c> (one history) as the
    /// owner role. EF Core refuses to run when the model has changes no migration covers.
    /// </summary>
    public static async Task RunAsync(
        string connectionString,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        await using var db = CreateDbContext(connectionString);
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
        foreach (var migration in pending)
        {
            await output.WriteLineAsync($"Applying {migration}");
        }

        await db.Database.MigrateAsync(cancellationToken);
        await output.WriteLineAsync(
            pending.Length == 0
                ? "The core schema is up to date."
                : $"Applied {pending.Length} migration(s)."
        );
    }

    // No tenant interceptor: migrations run DDL outside tenant transactions.
    internal static CoreDbContext CreateDbContext(string connectionString) =>
        new(new DbContextOptionsBuilder<CoreDbContext>().UseCoreDatabase(connectionString).Options);
}
