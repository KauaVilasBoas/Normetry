using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Normetry.Application.Abstractions;
using Normetry.Infrastructure.Persistence;
using Normetry.IntegrationTests.Database;
using Xunit;

namespace Normetry.IntegrationTests.Messaging;

/// <summary>A background scope that forgets its tenant, or sets its tenant or user too late, fails closed and loudly.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class BackgroundTenantTests
{
    private readonly DatabaseFixture db;

    public BackgroundTenantTests(DatabaseFixture db)
    {
        db.SkipWithoutDocker();
        this.db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_background_scope_without_a_tenant_throws_on_its_first_query()
    {
        await using var scope = db.Api.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<CoreDbContext>();

        // No HTTP request either: the token's tenant source has no user.
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(Ct);
            await context.Establishments.CountAsync(Ct);
        });
    }

    [Fact]
    public async Task A_tenant_set_after_the_scope_resolved_its_tenant_source_throws()
    {
        await using var scope = db.Api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<CoreDbContext>();

        Assert.Throws<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<BackgroundTenant>().Set(Guid.NewGuid())
        );
    }

    [Fact]
    public async Task A_user_set_after_the_scope_resolved_its_user_source_throws()
    {
        await using var scope = db.Api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ICurrentUser>();

        Assert.Throws<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<BackgroundUser>().Set("demo-owner")
        );
    }
}
