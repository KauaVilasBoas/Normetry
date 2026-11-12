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
