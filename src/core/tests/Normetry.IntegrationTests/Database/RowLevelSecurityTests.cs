using Xunit;

namespace Normetry.IntegrationTests.Database;

/// <summary>
/// RLS on core.tenants and core.establishments, in plain SQL as app_core (the rls-table skill).
/// Every test makes its own tenants, so tests never see each other's rows.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RowLevelSecurityTests
{
    private const string InsertTenant = "INSERT INTO core.tenants (id, name) VALUES ($1, 'Tenant')";
    private const string InsertEstablishment = """
        INSERT INTO core.establishments (id, tenant_id, name, type, jurisdiction)
        VALUES ($1, $2, 'Kitchen', 'Restaurant', 'US')
        """;
    private const string VisibleEstablishments =
        "SELECT id FROM core.establishments WHERE id = ANY($1)";

    private readonly DatabaseFixture db;

    public RowLevelSecurityTests(DatabaseFixture db)
    {
        db.SkipWithoutDocker();
        this.db = db;
    }

    private string AppCore => db.ConnectionString("app_core");

    /// <summary>Two new tenants with one establishment each, written by app_core.</summary>
    private async Task<(
        Guid TenantA,
        Guid TenantB,
        Guid EstablishmentA,
        Guid EstablishmentB
    )> SeedAsync()
    {
        var (tenantA, tenantB) = (Guid.NewGuid(), Guid.NewGuid());
        var (establishmentA, establishmentB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        foreach (
            var (tenant, establishment) in new[]
            {
                (tenantA, establishmentA),
                (tenantB, establishmentB),
            }
        )
        {
            await using var session = await Session.BeginAsync(AppCore, tenant);
            await session.ExecuteAsync(InsertTenant, tenant);
            await session.ExecuteAsync(InsertEstablishment, establishment, tenant);
            await session.CommitAsync();
        }
        return (tenantA, tenantB, establishmentA, establishmentB);
    }

    [Fact]
    public async Task Each_tenant_reads_only_its_own_establishments()
    {
        var (tenantA, tenantB, establishmentA, establishmentB) = await SeedAsync();
        Guid[] both = [establishmentA, establishmentB];

        await using (var session = await Session.BeginAsync(AppCore, tenantA))
        {
            Assert.Equal(
                [establishmentA],
                await session.ColumnAsync<Guid>(VisibleEstablishments, both)
            );
        }
        await using (var session = await Session.BeginAsync(AppCore, tenantB))
        {
            Assert.Equal(
                [establishmentB],
                await session.ColumnAsync<Guid>(VisibleEstablishments, both)
            );
        }
    }

    [Fact]
    public async Task A_tenant_cannot_update_or_delete_another_tenants_establishments()
    {
        var (tenantA, tenantB, establishmentA, _) = await SeedAsync();

        await using (var session = await Session.BeginAsync(AppCore, tenantB))
        {
            var updated = await session.ExecuteAsync(
                "UPDATE core.establishments SET name = 'Taken' WHERE id = $1",
                establishmentA
            );
            var deleted = await session.ExecuteAsync(
                "DELETE FROM core.establishments WHERE id = $1",
                establishmentA
            );
            Assert.Equal((0, 0), (updated, deleted));
            await session.CommitAsync();
        }

        await using (var session = await Session.BeginAsync(AppCore, tenantA))
        {
            var name = await session.ScalarAsync<string>(
                "SELECT name FROM core.establishments WHERE id = $1",
                establishmentA
            );
            Assert.Equal("Kitchen", name);
        }
    }

    [Fact]
    public async Task A_tenant_cannot_insert_or_move_rows_into_another_tenant()
    {
        var (tenantA, tenantB, establishmentA, _) = await SeedAsync();

        await using (var session = await Session.BeginAsync(AppCore, tenantA))
        {
            await session.AssertDeniedAsync(InsertEstablishment, Guid.CreateVersion7(), tenantB);
        }
        await using (var session = await Session.BeginAsync(AppCore, tenantA))
        {
            await session.AssertDeniedAsync(
                "UPDATE core.establishments SET tenant_id = $1 WHERE id = $2",
                tenantB,
                establishmentA
            );
        }
    }

    [Fact]
    public async Task Without_a_tenant_no_establishment_is_visible_and_writes_fail()
    {
        var (tenantA, _, establishmentA, establishmentB) = await SeedAsync();

        await using (var session = await Session.BeginAsync(AppCore))
        {
            Assert.Equal(
                0L,
                await session.ScalarAsync<long>("SELECT count(*) FROM core.establishments")
            );
            Assert.Equal(
                0,
                await session.ExecuteAsync("UPDATE core.establishments SET name = 'x'")
            );
            Assert.Equal(0, await session.ExecuteAsync("DELETE FROM core.establishments"));
            await session.CommitAsync();
        }
        await using (var session = await Session.BeginAsync(AppCore))
        {
            await session.AssertDeniedAsync(InsertEstablishment, Guid.CreateVersion7(), tenantA);
        }

        await using var tenantSession = await Session.BeginAsync(AppCore, tenantA);
        Assert.Equal(
            [establishmentA],
            await tenantSession.ColumnAsync<Guid>(
                VisibleEstablishments,
                new[] { establishmentA, establishmentB }
            )
        );
    }

    [Fact]
    public async Task A_session_sees_only_its_own_tenant_row()
    {
        var (tenantA, tenantB, _, _) = await SeedAsync();
        const string Visible = "SELECT id FROM core.tenants WHERE id = ANY($1)";
        Guid[] both = [tenantA, tenantB];

        await using (var session = await Session.BeginAsync(AppCore, tenantA))
        {
            Assert.Equal([tenantA], await session.ColumnAsync<Guid>(Visible, both));
        }
        await using (var session = await Session.BeginAsync(AppCore))
        {
            Assert.Equal(0L, await session.ScalarAsync<long>("SELECT count(*) FROM core.tenants"));
        }
    }

    [Theory]
    [InlineData("another tenant", InsertTenant)]
    [InlineData("no tenant", InsertTenant)]
    [InlineData("update", "UPDATE core.tenants SET name = 'Renamed' WHERE id = $1")]
    [InlineData("delete", "DELETE FROM core.tenants WHERE id = $1")]
    public async Task App_core_cannot_create_other_tenants_or_change_any_tenant(
        string attempt,
        string sql
    )
    {
        var (tenantA, _, _, _) = await SeedAsync();
        // Inserts use a new id; update and delete target the session's own tenant row.
        var (sessionTenant, target) = attempt switch
        {
            "another tenant" => ((Guid?)tenantA, Guid.NewGuid()),
            "no tenant" => (null, Guid.NewGuid()),
            _ => (tenantA, tenantA),
        };

        await using var session = await Session.BeginAsync(AppCore, sessionTenant);
        await session.AssertDeniedAsync(sql, target);
    }

    [Fact]
    public async Task The_owner_sees_and_writes_no_rows_even_with_a_tenant_set()
    {
        // FORCE ROW LEVEL SECURITY with no policy for core_owner.
        var (tenantA, _, establishmentA, _) = await SeedAsync();
        var owner = db.ConnectionString("core_owner");

        await using (var session = await Session.BeginAsync(owner, tenantA))
        {
            Assert.Empty(
                await session.ColumnAsync<Guid>(VisibleEstablishments, new[] { establishmentA })
            );
            Assert.Equal(0L, await session.ScalarAsync<long>("SELECT count(*) FROM core.tenants"));
        }
        await using (var session = await Session.BeginAsync(owner, tenantA))
        {
            await session.AssertDeniedAsync(InsertEstablishment, Guid.CreateVersion7(), tenantA);
        }
    }
}
