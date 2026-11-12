using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Normetry.Application.Abstractions;
using Normetry.Domain.Establishments;
using Normetry.Domain.Tenancy;

namespace Normetry.Infrastructure.Persistence;

/// <summary>
/// Creates the two demo tenants of the local Keycloak realm (<c>deploy/keycloak/README.md</c>)
/// and their demo establishments, one per jurisdiction across the two, when the API starts in
/// Development. Idempotent: a tenant or an establishment name that exists is left alone. It
/// connects as <c>app_core</c> like any request, in one transaction scoped to each tenant: the
/// <c>core.tenants</c> policies let a session read and insert only its own row, so no owner role
/// or RLS bypass is involved.
/// </summary>
public sealed class DemoTenantSeed(
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<DemoTenantSeed> logger
) : IHostedService
{
    // Same ids as the tenant_id attributes in deploy/keycloak/normetry-realm.json.
    internal static readonly (
        Guid Id,
        string Name,
        (string Name, Jurisdiction Jurisdiction)[] Establishments
    )[] Tenants =
    [
        (
            Guid.Parse("11111111-1111-4111-8111-111111111111"),
            "Demo Bistro",
            [("Bistro São Paulo", Jurisdiction.BR), ("Bistro Lisboa", Jurisdiction.EU)]
        ),
        (
            Guid.Parse("22222222-2222-4222-8222-222222222222"),
            "Demo Bakery",
            [("Bakery Austin", Jurisdiction.US)]
        ),
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Demo data must never reach a deployed database.
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("The demo tenant seed runs only in Development.");
        }

        var connectionString = CoreDatabase.GetConnectionString(configuration);
        foreach (var (id, name, establishments) in Tenants)
        {
            var options = new DbContextOptionsBuilder<CoreDbContext>()
                .UseTenantScopedCoreDatabase(connectionString, new SeedTenant(id))
                .Options;
            await using var db = new CoreDbContext(options);
            await using var transaction = await db.Database.BeginTransactionAsync(
                cancellationToken
            );
            if (!await db.Tenants.AnyAsync(tenant => tenant.Id == id, cancellationToken))
            {
                db.Tenants.Add(Tenant.Create(id, name));
                logger.LogInformation("Seeding demo tenant {TenantName}", name);
            }

            // RLS shows only this tenant's establishments.
            var existing = await db
                .Establishments.Select(establishment => establishment.Name)
                .ToListAsync(cancellationToken);
            foreach (var (establishment, jurisdiction) in establishments)
            {
                if (!existing.Contains(establishment))
                {
                    db.Establishments.Add(
                        Establishment.Create(
                            id,
                            establishment,
                            EstablishmentType.Restaurant,
                            jurisdiction
                        )
                    );
                    logger.LogInformation(
                        "Seeding demo establishment {EstablishmentName}",
                        establishment
                    );
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private sealed record SeedTenant(Guid TenantId) : ITenantContext;
}
