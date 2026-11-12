using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Normetry.Application.Abstractions;
using Normetry.Domain.Tenancy;

namespace Normetry.Infrastructure.Persistence;

/// <summary>
/// Creates the two demo tenants of the local Keycloak realm (<c>deploy/keycloak/README.md</c>)
/// when the API starts in Development. Idempotent. It connects as <c>app_core</c> like any
/// request, in one transaction scoped to each tenant: the <c>core.tenants</c> policies let a
/// session read and insert only its own row, so no owner role or RLS bypass is involved.
/// </summary>
public sealed class DemoTenantSeed(
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<DemoTenantSeed> logger
) : IHostedService
{
    // Same ids as the tenant_id attributes in deploy/keycloak/normetry-realm.json.
    internal static readonly (Guid Id, string Name)[] Tenants =
    [
        (Guid.Parse("11111111-1111-4111-8111-111111111111"), "Demo Bistro"),
        (Guid.Parse("22222222-2222-4222-8222-222222222222"), "Demo Bakery"),
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Demo data must never reach a deployed database.
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("The demo tenant seed runs only in Development.");
        }

        var connectionString = CoreDatabase.GetConnectionString(configuration);
        foreach (var (id, name) in Tenants)
        {
            var options = new DbContextOptionsBuilder<CoreDbContext>()
                .UseTenantScopedCoreDatabase(connectionString, new SeedTenant(id))
                .Options;
            await using var db = new CoreDbContext(options);
            await using var transaction = await db.Database.BeginTransactionAsync(
                cancellationToken
            );
            if (await db.Tenants.AnyAsync(tenant => tenant.Id == id, cancellationToken))
            {
                continue;
            }

            db.Tenants.Add(Tenant.Create(id, name));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            logger.LogInformation("Seeded demo tenant {TenantName}", name);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private sealed record SeedTenant(Guid TenantId) : ITenantContext;
}
