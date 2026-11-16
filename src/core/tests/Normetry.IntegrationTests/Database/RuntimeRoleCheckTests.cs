using Microsoft.Extensions.Configuration;
using Normetry.Infrastructure.Persistence;
using Xunit;

namespace Normetry.IntegrationTests.Database;

/// <summary>The API refuses to start on a role that can get around RLS.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class RuntimeRoleCheckTests
{
    private readonly DatabaseFixture db;

    public RuntimeRoleCheckTests(DatabaseFixture db)
    {
        db.SkipWithoutDocker();
        this.db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public Task App_core_passes() =>
        RuntimeRoleCheck.EnsureAsync(db.ConnectionString("app_core"), Ct);

    [Fact]
    public Task Core_dispatcher_passes() =>
        RuntimeRoleCheck.EnsureAsync(db.ConnectionString("core_dispatcher"), Ct);

    [Fact]
    public async Task The_API_refuses_a_privileged_role_on_the_tenant_list_connection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:core"] = db.ConnectionString("app_core"),
                    ["ConnectionStrings:core-dispatcher"] = db.ConnectionString("core_owner"),
                }
            )
            .Build();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeRoleCheck(configuration).StartAsync(Ct)
        );

        Assert.Contains("ConnectionStrings:core-dispatcher", error.Message);
        Assert.Contains("owns schema core", error.Message);
    }

    [Fact]
    public async Task The_core_schema_owner_is_refused()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RuntimeRoleCheck.EnsureAsync(db.ConnectionString("core_owner"), Ct)
        );

        Assert.Contains("owns schema core", error.Message);
    }

    [Fact]
    public async Task A_superuser_is_refused()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RuntimeRoleCheck.EnsureAsync(db.SuperuserConnectionString, Ct)
        );

        Assert.Contains("is a superuser", error.Message);
    }
}
