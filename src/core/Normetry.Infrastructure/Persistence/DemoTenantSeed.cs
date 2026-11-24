using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Normetry.Domain.Establishments;
using Normetry.Domain.Identity;
using Normetry.Domain.Tenancy;

namespace Normetry.Infrastructure.Persistence;

/// <summary>
/// Creates the two demo tenants of the local Keycloak realm (<c>deploy/keycloak/README.md</c>)
/// when the API starts in Development, each in one transaction with its system profiles (Owner,
/// Manager, Auditor), its demo users and their profiles, and its demo establishments, one per
/// jurisdiction across the two. For a tenant that exists, it is also the startup step that adds
/// the system profiles it lacks and the inspection permissions its system profiles predate. Idempotent: a tenant, system profile, user (by <c>sub</c>) or
/// establishment name that exists is left alone. It connects as <c>app_core</c> like any request,
/// scoped to each tenant: the <c>core.tenants</c> policies let a session read and insert only its
/// own row, so no owner role or RLS bypass is involved.
/// </summary>
public sealed class DemoTenantSeed(
    IServiceScopeFactory scopes,
    IHostEnvironment environment,
    ILogger<DemoTenantSeed> logger
) : IHostedService
{
    private static readonly string InspectionsArea = Permissions.AreaOf(
        Permissions.Inspections.Read
    );

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

        foreach (var tenant in Tenants)
        {
            // The tenant's own row passes the core.tenants policies too, so all four steps run in
            // one transaction under its tenant.
            await using var scope = TenantScope.Open(scopes, logger, tenant.Id);
            var db = scope.Services.GetRequiredService<CoreDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(
                cancellationToken
            );
            await EnsureTenantRowAsync(db, tenant, cancellationToken);
            var profiles = await EnsureSystemProfilesAsync(db, tenant, cancellationToken);
            await EnsureDemoUsersAsync(db, tenant, profiles, cancellationToken);
            await EnsureDemoEstablishmentsAsync(db, tenant, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureTenantRowAsync(
        CoreDbContext db,
        DemoTenant tenant,
        CancellationToken cancellationToken
    )
    {
        if (!await db.Tenants.AnyAsync(row => row.Id == tenant.Id, cancellationToken))
        {
            db.Tenants.Add(Tenant.Create(tenant.Id, tenant.Name));
            logger.LogInformation("Seeding demo tenant {TenantName}", tenant.Name);
        }
    }

    /// <summary>
    /// Adds the system profiles the tenant lacks and returns all of them, each with the inspection
    /// defaults it may predate. RLS shows only this tenant's profiles.
    /// </summary>
    private async Task<List<Profile>> EnsureSystemProfilesAsync(
        CoreDbContext db,
        DemoTenant tenant,
        CancellationToken cancellationToken
    )
    {
        var profiles = await db
            .Profiles.Include(profile => profile.GrantedPermissions)
            .Where(profile => profile.SystemKey != null)
            .ToListAsync(cancellationToken);
        foreach (var key in Profile.SystemKeys.All)
        {
            if (!profiles.Any(profile => profile.SystemKey == key))
            {
                var profile = Profile.CreateSystem(tenant.Id, key);
                db.Profiles.Add(profile);
                profiles.Add(profile);
                logger.LogInformation("Seeding system profile {ProfileName}", profile.Name);
            }
        }
        foreach (var profile in profiles)
        {
            GrantInspectionDefaults(profile);
        }
        return profiles;
    }

    /// <summary>Each demo user the tenant lacks (by <c>sub</c>), with its one system profile.</summary>
    private async Task EnsureDemoUsersAsync(
        CoreDbContext db,
        DemoTenant tenant,
        List<Profile> profiles,
        CancellationToken cancellationToken
    )
    {
        var existing = await db.Users.Select(user => user.Sub).ToListAsync(cancellationToken);
        foreach (var (sub, displayName, email, systemKey) in tenant.Users)
        {
            if (!existing.Contains(sub))
            {
                var user = User.Create(tenant.Id, sub, displayName, email);
                user.Assign(profiles.Single(profile => profile.SystemKey == systemKey));
                db.Users.Add(user);
                logger.LogInformation("Seeding demo user {DisplayName}", displayName);
            }
        }
    }

    /// <summary>Each demo establishment the tenant lacks, by name.</summary>
    private async Task EnsureDemoEstablishmentsAsync(
        CoreDbContext db,
        DemoTenant tenant,
        CancellationToken cancellationToken
    )
    {
        var existing = await db
            .Establishments.Select(establishment => establishment.Name)
            .ToListAsync(cancellationToken);
        foreach (var (name, jurisdiction) in tenant.Establishments)
        {
            if (!existing.Contains(name))
            {
                db.Establishments.Add(
                    Establishment.Create(
                        tenant.Id,
                        name,
                        EstablishmentType.Restaurant,
                        jurisdiction
                    )
                );
                logger.LogInformation("Seeding demo establishment {EstablishmentName}", name);
            }
        }
    }

    /// <summary>
    /// A system profile seeded before the inspection permissions existed gets its defaults of
    /// them. One that holds any inspection permission is left as the tenant set it, so a later
    /// change in Settings survives the next start.
    /// </summary>
    private void GrantInspectionDefaults(Profile profile)
    {
        var granted = profile.GrantedPermissions.Select(granted => granted.Permission).ToList();
        if (profile.GrantsAll || granted.Any(IsInspectionPermission))
        {
            return;
        }
        var defaults = Profile
            .DefaultPermissions(profile.SystemKey!)
            .Where(IsInspectionPermission)
            .ToList();
        profile.SetPermissions(granted.Concat(defaults));
        logger.LogInformation(
            "Granting the inspection permissions to system profile {ProfileName}",
            profile.Name
        );
    }

    private static bool IsInspectionPermission(string name) =>
        Permissions.AreaOf(name) == InspectionsArea;

    internal sealed record DemoTenant(
        Guid Id,
        string Name,
        (string Name, Jurisdiction Jurisdiction)[] Establishments,
        DemoUser[] Users
    );

    /// <summary><paramref name="Sub"/> is the Keycloak user id; the user holds one system profile.</summary>
    internal sealed record DemoUser(string Sub, string DisplayName, string Email, string SystemKey);
}
