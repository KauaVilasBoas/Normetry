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
    public async Task The_demo_seed_gives_each_tenant_its_system_profiles_and_demo_users_once()
    {
        var seed = db.Api.Services.GetServices<IHostedService>().OfType<DemoTenantSeed>().Single();

        await seed.StartAsync(Ct);

        foreach (
            var (tenant, users) in new[]
            {
                (
                    ApiFactory.TenantA,
                    new[]
                    {
                        $"{ApiFactory.BistroOwner} Owner",
                        $"{ApiFactory.BistroManager} Manager",
                    }
                ),
                (
                    ApiFactory.TenantB,
                    new[]
                    {
                        $"{ApiFactory.BakeryOwner} Owner",
                        $"{ApiFactory.BakeryAuditor} Auditor",
                    }
                ),
            }
        )
        {
            await using var session = await Session.BeginAsync(
                db.ConnectionString("app_core"),
                Guid.Parse(tenant)
            );
            Assert.Equal(
                ["auditor Auditor false", "manager Manager false", "owner Owner true"],
                await session.ColumnAsync<string>(
                    """
                    SELECT system_key || ' ' || name || ' ' || grants_all FROM core.profiles
                    WHERE system_key IS NOT NULL ORDER BY system_key
                    """
                )
            );
            Assert.Equal(
                users,
                await session.ColumnAsync<string>(
                    """
                    SELECT u.sub || ' ' || p.name FROM core.users u
                    JOIN core.user_profiles up ON up.user_id = u.id
                    JOIN core.profiles p ON p.id = up.profile_id
                    WHERE u.sub = ANY($1) ORDER BY u.sub
                    """,
                    (object)users.Select(user => user.Split(' ')[0]).ToArray()
                )
            );
        }
    }

    [Fact]
    public async Task The_system_profiles_grant_what_the_design_says()
    {
        await using var session = await Session.BeginAsync(
            db.ConnectionString("app_core"),
            Guid.Parse(ApiFactory.TenantA)
        );

        var granted = await session.ColumnAsync<string>(
            """
            SELECT p.system_key || ' ' || pp.permission FROM core.profiles p
            JOIN core.profile_permissions pp ON pp.profile_id = p.id
            WHERE p.system_key IS NOT NULL ORDER BY (p.system_key || ' ' || pp.permission) COLLATE "C"
            """
        );

        // Owner has no rows: grants_all covers the whole catalog.
        Assert.Equal(
            [
                "auditor chat.ask",
                "auditor documents.read",
                "auditor establishments.read",
                "auditor inspections.read",
                "auditor profiles.read",
                "manager chat.ask",
                "manager documents.delete",
                "manager documents.read",
                "manager documents.upload",
                "manager establishments.manage",
                "manager establishments.read",
                "manager inspections.read",
                "manager inspections.review",
                "manager inspections.run",
                "manager profiles.read",
            ],
            granted
        );
    }

    [Fact]
    public async Task The_demo_seed_gives_older_system_profiles_the_inspection_permissions_once()
    {
        // Bakery's Auditor as seeded before the inspection permissions existed, and Bistro's
        // Manager after someone took inspections.run away in Settings.
        var seed = db.Api.Services.GetServices<IHostedService>().OfType<DemoTenantSeed>().Single();
        const string Granted = """
            SELECT pp.permission FROM core.profile_permissions pp
            JOIN core.profiles p ON p.id = pp.profile_id
            WHERE p.system_key = $1 AND pp.permission LIKE 'inspections.%'
            ORDER BY pp.permission COLLATE "C"
            """;
        const string Revoke = """
            DELETE FROM core.profile_permissions pp USING core.profiles p
            WHERE p.id = pp.profile_id AND p.system_key = $1 AND pp.permission LIKE $2
            """;
        var (bakery, bistro) = (Guid.Parse(ApiFactory.TenantB), Guid.Parse(ApiFactory.TenantA));
        await using (
            var session = await Session.BeginAsync(db.ConnectionString("app_core"), bakery)
        )
        {
            await session.ExecuteAsync(Revoke, "auditor", "inspections.%");
            await session.CommitAsync();
        }
        await using (
            var session = await Session.BeginAsync(db.ConnectionString("app_core"), bistro)
        )
        {
            await session.ExecuteAsync(Revoke, "manager", "inspections.run");
            await session.CommitAsync();
        }

        try
        {
            await seed.StartAsync(Ct);
            await seed.StartAsync(Ct);

            await using (
                var session = await Session.BeginAsync(db.ConnectionString("app_core"), bakery)
            )
            {
                Assert.Equal(
                    ["inspections.read"],
                    await session.ColumnAsync<string>(Granted, "auditor")
                );
            }
            await using (
                var session = await Session.BeginAsync(db.ConnectionString("app_core"), bistro)
            )
            {
                Assert.Equal(
                    ["inspections.read", "inspections.review"],
                    await session.ColumnAsync<string>(Granted, "manager")
                );
            }
        }
        finally
        {
            // Back to the defaults, for the tests that read the demo profiles.
            foreach (
                var (tenant, key, permission) in new[]
                {
                    (bistro, "manager", "inspections.run"),
                    (bakery, "auditor", "inspections.read"),
                }
            )
            {
                await using var session = await Session.BeginAsync(
                    db.ConnectionString("app_core"),
                    tenant
                );
                await session.ExecuteAsync(
                    """
                    INSERT INTO core.profile_permissions
                    SELECT tenant_id, id, $2 FROM core.profiles WHERE system_key = $1
                    ON CONFLICT DO NOTHING
                    """,
                    key,
                    permission
                );
                await session.CommitAsync();
            }
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
