using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Normetry.Application.Identity.GetCurrentUserPermissions;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.Domain.Identity;
using Normetry.IntegrationTests.Database;
using Xunit;

namespace Normetry.IntegrationTests.Identity;

/// <summary>
/// The caller's user row (created on first sign-in through <c>/me</c>) and what their profiles
/// grant, against the test database as app_core.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class CurrentUserTests
{
    private readonly DatabaseFixture db;

    public CurrentUserTests(DatabaseFixture db)
    {
        db.SkipWithoutDocker();
        this.db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Client(string tenant, string sub, string? name = null) =>
        db.Api.CreateClient(
            ApiFactory.CreateToken(token =>
            {
                token.Claims["tenant_id"] = tenant;
                token.Claims["sub"] = sub;
                if (name is not null)
                {
                    token.Claims["name"] = name;
                    token.Claims["email"] = $"{name.Replace(' ', '.').ToLowerInvariant()}@test";
                }
            })
        );

    private async Task<List<string>> UsersAsync(Guid tenant, string sub)
    {
        await using var session = await Session.BeginAsync(db.ConnectionString("app_core"), tenant);
        return await session.ColumnAsync<string>(
            """
            SELECT coalesce(display_name, '-') || ' ' || coalesce(email, '-') FROM core.users
            WHERE sub = $1
            """,
            sub
        );
    }

    [Fact]
    public async Task The_first_sign_in_creates_the_user_without_profiles_and_later_ones_refresh_it()
    {
        var tenant = Guid.NewGuid();
        await using (
            var session = await Session.BeginAsync(db.ConnectionString("app_core"), tenant)
        )
        {
            await session.ExecuteAsync("INSERT INTO core.tenants VALUES ($1, 'Tenant')", tenant);
            await session.CommitAsync();
        }
        var sub = Guid.NewGuid().ToString();

        using (var first = Client(tenant.ToString(), sub, "Rita Cook"))
        {
            (await first.GetAsync("/me", Ct)).EnsureSuccessStatusCode();
            (await first.GetAsync("/me", Ct)).EnsureSuccessStatusCode();
        }
        Assert.Equal(["Rita Cook rita.cook@test"], await UsersAsync(tenant, sub));

        using (var renamed = Client(tenant.ToString(), sub, "Rita Chef"))
        {
            (await renamed.GetAsync("/me", Ct)).EnsureSuccessStatusCode();
        }
        Assert.Equal(["Rita Chef rita.chef@test"], await UsersAsync(tenant, sub));
        Assert.Empty(await PermissionsAsync(tenant.ToString(), sub));
    }

    [Fact]
    public async Task A_tenant_the_core_has_no_row_for_gets_no_user_row()
    {
        var tenant = Guid.NewGuid();
        var sub = Guid.NewGuid().ToString();
        using var client = Client(tenant.ToString(), sub, "Nobody");

        var response = await client.GetAsync("/me", Ct);

        response.EnsureSuccessStatusCode();
        Assert.Empty(await UsersAsync(tenant, sub));
    }

    [Fact]
    public async Task Each_demo_user_gets_what_their_system_profile_grants()
    {
        Assert.Equal(
            Permissions.All.Order(StringComparer.Ordinal),
            await PermissionsAsync(ApiFactory.TenantA, ApiFactory.BistroOwner)
        );
        Assert.Equal(
            Permissions
                .All.Where(name => name != Permissions.Profiles.Manage)
                .Order(StringComparer.Ordinal),
            await PermissionsAsync(ApiFactory.TenantA, ApiFactory.BistroManager)
        );
        Assert.Equal(
            [
                Permissions.Chat.Ask,
                Permissions.Documents.Read,
                Permissions.Establishments.Read,
                Permissions.Profiles.Read,
            ],
            await PermissionsAsync(ApiFactory.TenantB, ApiFactory.BakeryAuditor)
        );
        // The same sub in another tenant is another user, with nothing there.
        Assert.Empty(await PermissionsAsync(ApiFactory.TenantB, ApiFactory.BistroOwner));
    }

    [Fact]
    public async Task A_permission_added_to_the_catalog_reaches_Owner_with_no_other_change()
    {
        const string Added = "reports.export";
        var owner = db.ConnectionString("core_owner");
        await using (var session = await Session.BeginAsync(owner))
        {
            await session.ExecuteAsync("INSERT INTO core.permissions VALUES ($1)", Added);
            await session.CommitAsync();
        }

        try
        {
            Assert.Contains(
                Added,
                await PermissionsAsync(ApiFactory.TenantB, ApiFactory.BakeryOwner)
            );
            Assert.DoesNotContain(
                Added,
                await PermissionsAsync(ApiFactory.TenantB, ApiFactory.BakeryAuditor)
            );
        }
        finally
        {
            await using var session = await Session.BeginAsync(owner);
            await session.ExecuteAsync("DELETE FROM core.permissions WHERE name = $1", Added);
            await session.CommitAsync();
        }
    }

    /// <summary>The query the permission checks run, through the real pipeline and RLS.</summary>
    private async Task<IReadOnlyList<string>> PermissionsAsync(string tenant, string sub)
    {
        using var scope = db.Api.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext =
            new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity([new("sub", sub), new("tenant_id", tenant)], "test")
                ),
            };
        var result = await scope
            .ServiceProvider.GetRequiredService<IDispatcher>()
            .QueryAsync(new GetCurrentUserPermissionsQuery(), Ct);
        return result.Value;
    }
}
