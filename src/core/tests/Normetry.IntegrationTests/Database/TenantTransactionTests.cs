using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Normetry.Application.Abstractions;
using Normetry.Infrastructure.Persistence;
using Npgsql;
using Xunit;

namespace Normetry.IntegrationTests.Database;

/// <summary>The runtime context: the interceptor sets the tenant per transaction, the guard rejects work outside one.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class TenantTransactionTests
{
    private readonly DatabaseFixture db;

    public TenantTransactionTests(DatabaseFixture db)
    {
        db.SkipWithoutDocker();
        this.db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private CoreDbContext Context(Guid tenantId, string? connectionString = null) =>
        new(
            new DbContextOptionsBuilder<CoreDbContext>()
                .UseTenantScopedCoreDatabase(
                    connectionString ?? db.ConnectionString("app_core"),
                    new FixedTenant(tenantId)
                )
                .Options
        );

    private async Task<Guid> NewTenantAsync()
    {
        var tenant = Guid.NewGuid();
        await using var session = await Session.BeginAsync(db.ConnectionString("app_core"), tenant);
        await session.ExecuteAsync(
            "INSERT INTO core.tenants (id, name) VALUES ($1, 'Tenant')",
            tenant
        );
        await session.CommitAsync();
        return tenant;
    }

    [Fact]
    public async Task A_command_outside_a_transaction_is_rejected()
    {
        await using var context = Context(await NewTenantAsync());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Establishments.CountAsync(Ct)
        );

        Assert.Contains("outside a transaction", error.Message);
    }

    [Fact]
    public async Task Every_transaction_is_scoped_to_the_contexts_tenant()
    {
        var (tenantA, tenantB) = (await NewTenantAsync(), await NewTenantAsync());

        await using (var context = Context(tenantA))
        {
            await using var transaction = await context.Database.BeginTransactionAsync(Ct);
            var tenants = await context.Tenants.Select(tenant => tenant.Id).ToListAsync(Ct);
            Assert.Equal([tenantA], tenants);
        }
        await using (var context = Context(tenantB))
        {
            await using var transaction = await context.Database.BeginTransactionAsync(Ct);
            var tenants = await context.Tenants.Select(tenant => tenant.Id).ToListAsync(Ct);
            Assert.Equal([tenantB], tenants);
        }
    }

    [Fact]
    public async Task The_tenant_does_not_outlive_its_transaction_on_the_same_connection()
    {
        var tenant = await NewTenantAsync();
        await using var context = Context(tenant);
        // Held open, so the pool cannot reset the session between the transaction and the checks.
        await context.Database.OpenConnectionAsync(Ct);
        await using (var transaction = await context.Database.BeginTransactionAsync(Ct))
        {
            Assert.Equal([tenant], await context.Tenants.Select(t => t.Id).ToListAsync(Ct));
            await transaction.CommitAsync(Ct);
        }

        // Plain ADO.NET on the same session, outside any transaction and outside the guard.
        async Task<object?> ScalarAsync(string sql)
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            return await command.ExecuteScalarAsync(Ct);
        }
        Assert.Equal(
            "",
            await ScalarAsync("SELECT coalesce(current_setting('app.tenant_id', true), '')")
        );
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM core.tenants"));
    }

    [Fact]
    public async Task A_transaction_already_scoped_to_another_tenant_is_rejected()
    {
        var (tenantA, tenantB) = (await NewTenantAsync(), await NewTenantAsync());
        await using var connection = new NpgsqlConnection(db.ConnectionString("app_core"));
        await connection.OpenAsync(Ct);
        await using var transaction = await connection.BeginTransactionAsync(Ct);
        await using (
            var setTenant = new NpgsqlCommand(
                "SELECT set_config('app.tenant_id', $1, true)",
                connection,
                transaction
            )
        )
        {
            setTenant.Parameters.Add(new NpgsqlParameter { Value = tenantA.ToString() });
            await setTenant.ExecuteNonQueryAsync(Ct);
        }

        await using var context = Context(tenantB);
        context.Database.SetDbConnection(connection);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.Database.UseTransactionAsync(transaction, Ct)
        );

        Assert.Contains("another tenant", error.Message);
    }

    [Fact]
    public async Task The_demo_seed_creates_the_realm_tenants_and_their_establishments_once()
    {
        var seed = db.Api.Services.GetServices<IHostedService>().OfType<DemoTenantSeed>().Single();

        // The API ran it once at startup; a second run must change nothing.
        await seed.StartAsync(Ct);

        foreach (
            var (tenant, expected) in new[]
            {
                (ApiFactory.TenantA, new[] { "Bistro Lisboa EU", "Bistro São Paulo BR" }),
                (ApiFactory.TenantB, new[] { "Bakery Austin US" }),
            }
        )
        {
            await using var session = await Session.BeginAsync(
                db.ConnectionString("app_core"),
                Guid.Parse(tenant)
            );
            Assert.Equal(1L, await session.ScalarAsync<long>("SELECT count(*) FROM core.tenants"));
            // Other tests add establishments of their own; the seeded names exist once each.
            var seeded = await session.ColumnAsync<string>(
                """
                SELECT name || ' ' || jurisdiction FROM core.establishments
                WHERE name = ANY($1) AND type = 'Restaurant' ORDER BY name
                """,
                (object)expected.Select(row => row[..row.LastIndexOf(' ')]).ToArray()
            );
            Assert.Equal(expected, seeded);
        }
    }

    [Fact]
    public void The_demo_seed_is_registered_only_in_Development()
    {
        using var production = db.Api.WithWebHostBuilder(builder =>
            builder.UseEnvironment("Production")
        );

        Assert.Empty(production.Services.GetServices<IHostedService>().OfType<DemoTenantSeed>());
    }

    private sealed record FixedTenant(Guid TenantId) : ITenantContext;
}
