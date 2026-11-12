using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Normetry.Application.Abstractions;

namespace Normetry.Infrastructure.Persistence;

public static class CoreDatabase
{
    public const string Schema = "core";

    /// <summary>Runtime connection string name: the <c>app_core</c> role.</summary>
    public const string ConnectionStringName = "core";

    public static string GetConnectionString(IConfiguration configuration) =>
        configuration.GetConnectionString(ConnectionStringName)
        ?? throw new InvalidOperationException(
            $"Set ConnectionStrings:{ConnectionStringName} (the app_core role)."
        );

    /// <summary>
    /// Npgsql, lower-case snake_case names, the migration history in
    /// <c>core."__EFMigrationsHistory"</c> and the migrations in <c>Normetry.Migrations</c>.
    /// No tenant, so it is internal: only <c>Normetry.Migrations</c> uses it directly.
    /// </summary>
    internal static TBuilder UseCoreDatabase<TBuilder>(
        this TBuilder options,
        string connectionString
    )
        where TBuilder : DbContextOptionsBuilder
    {
        options
            .UseNpgsql(connectionString, npgsql =>
                npgsql
                    .MigrationsAssembly("Normetry.Migrations")
                    .MigrationsHistoryTable(HistoryRepository.DefaultTableName, Schema)
            )
            .UseSnakeCaseNamingConvention();
        return options;
    }

    /// <summary>
    /// What every runtime context uses: each transaction starts by setting
    /// <c>app.tenant_id</c> to <paramref name="tenant"/>, and a command outside a transaction
    /// throws, so no SQL reaches tenant data without a tenant. The HTTP pipeline passes the
    /// tenant from the token; another host (a queue consumer) passes its own context.
    /// </summary>
    public static TBuilder UseTenantScopedCoreDatabase<TBuilder>(
        this TBuilder options,
        string connectionString,
        ITenantContext tenant
    )
        where TBuilder : DbContextOptionsBuilder
    {
        options
            .UseCoreDatabase(connectionString)
            .AddInterceptors(
                new TenantTransactionInterceptor(tenant),
                TransactionGuardInterceptor.Instance
            );
        return options;
    }
}
