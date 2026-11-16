using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Normetry.Application.Abstractions;
using Normetry.Domain.Establishments;
using Normetry.Domain.Identity;
using Normetry.Domain.Tenancy;

namespace Normetry.Infrastructure.Persistence;

/// <summary>
/// Creates the two demo tenants of the local Keycloak realm (<c>deploy/keycloak/README.md</c>)
/// when the API starts in Development, each in one transaction with its system profiles (Owner,
/// Manager, Auditor), its demo users and their profiles, and its demo establishments, one per
/// jurisdiction across the two. For a tenant that exists, it is also the startup step that adds
/// the system profiles it lacks. Idempotent: a tenant, system profile, user (by <c>sub</c>) or
/// establishment name that exists is left alone. It connects as <c>app_core</c> like any request,
/// scoped to each tenant: the <c>core.tenants</c> policies let a session read and insert only its
/// own row, so no owner role or RLS bypass is involved.
/// </summary>
public sealed class DemoTenantSeed(
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<DemoTenantSeed> logger
) : IHostedService
{
    // Same ids as the tenant_id attributes and the user ids in deploy/keycloak/normetry-realm.json,
    // so the token's sub finds its row.
    internal static readonly DemoTenant[] Tenants =
    [
        new(
            Guid.Parse("11111111-1111-4111-8111-111111111111"),
            "Demo Bistro",
            [("Bistro São Paulo", Jurisdiction.BR), ("Bistro Lisboa", Jurisdiction.EU)],
            [
                new(
                    "11111111-0000-4000-8000-000000000001",
                    "Olivia Owner",
                    "owner@bistro.example",
                    Profile.SystemKeys.Owner
                ),
                new(
                    "11111111-0000-4000-8000-000000000002",
                    "Marco Manager",
                    "manager@bistro.example",
                    Profile.SystemKeys.Manager
                ),
            ]
        ),
        new(
            Guid.Parse("22222222-2222-4222-8222-222222222222"),
            "Demo Bakery",
            [("Bakery Austin", Jurisdiction.US)],
            [
                new(
                    "22222222-0000-4000-8000-000000000001",
                    "Bruna Owner",
                    "owner@bakery.example",
                    Profile.SystemKeys.Owner
                ),
                new(
                    "22222222-0000-4000-8000-000000000002",
                    "Ana Auditor",
                    "auditor@bakery.example",
                    Profile.SystemKeys.Auditor
                ),
            ]
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
        foreach (var (id, name, establishments, users) in Tenants)
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

            // RLS shows only this tenant's rows from here on.
            var profiles = await db
                .Profiles.Where(profile => profile.SystemKey != null)
                .ToListAsync(cancellationToken);
            foreach (var key in Profile.SystemKeys.All)
            {
                if (!profiles.Any(profile => profile.SystemKey == key))
                {
                    var profile = Profile.CreateSystem(id, key);
                    db.Profiles.Add(profile);
                    profiles.Add(profile);
                    logger.LogInformation("Seeding system profile {ProfileName}", profile.Name);
                }
            }

            var existingUsers = await db
                .Users.Select(user => user.Sub)
                .ToListAsync(cancellationToken);
            foreach (var (sub, displayName, email, systemKey) in users)
            {
                if (!existingUsers.Contains(sub))
                {
                    var user = User.Create(id, sub, displayName, email);
                    user.Assign(profiles.Single(profile => profile.SystemKey == systemKey));
                    db.Users.Add(user);
                    logger.LogInformation("Seeding demo user {DisplayName}", displayName);
                }
            }

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

    internal sealed record DemoTenant(
        Guid Id,
        string Name,
        (string Name, Jurisdiction Jurisdiction)[] Establishments,
        DemoUser[] Users
    );

    /// <summary><paramref name="Sub"/> is the Keycloak user id; the user holds one system profile.</summary>
    internal sealed record DemoUser(string Sub, string DisplayName, string Email, string SystemKey);

    private sealed record SeedTenant(Guid TenantId) : ITenantContext;
}
