using Normetry.Domain.Establishments;

namespace Normetry.IntegrationTests.Database;

/// <summary>
/// Tenants, users, establishments and documents a test makes for itself, written in SQL as
/// app_core under RLS. Profiles get exactly the permissions given: no implied reads, unlike the API.
/// </summary>
public static class TestTenants
{
    /// <summary>A tenant, its Owner and one establishment (US).</summary>
    public sealed record Target(Guid Tenant, Guid Establishment, string Owner);

    /// <summary>
    /// A new tenant with its three system profiles, Owner held by one user per
    /// <paramref name="owners"/> sub.
    /// </summary>
    public static async Task<Guid> CreateAsync(
        DatabaseFixture db,
        Guid? id = null,
        params string[] owners
    )
    {
        var tenant = id ?? Guid.NewGuid();
        var owner = Guid.CreateVersion7();
        await using var session = await Session.BeginAsync(db.ConnectionString("app_core"), tenant);
        await session.ExecuteAsync(
            "INSERT INTO core.tenants (id, name) VALUES ($1, 'Tenant')",
            tenant
        );
        // Manager and Auditor exist like in a real tenant, but grant nothing here.
        await session.ExecuteAsync(
            """
            INSERT INTO core.profiles (tenant_id, id, name, system_key, grants_all)
            VALUES ($1, $2, 'Owner', 'owner', true),
                   ($1, $3, 'Manager', 'manager', false),
                   ($1, $4, 'Auditor', 'auditor', false)
            """,
            tenant,
            owner,
            Guid.CreateVersion7(),
            Guid.CreateVersion7()
        );
        foreach (var sub in owners)
        {
            await AssignAsync(session, tenant, sub, owner);
        }
        await session.CommitAsync();
        return tenant;
    }

    /// <summary>
    /// Gives the user <paramref name="sub"/> (created when missing) a new profile of
    /// <paramref name="tenant"/> granting exactly <paramref name="permissions"/>. Returns the profile id.
    /// </summary>
    public static async Task<Guid> GrantAsync(
        DatabaseFixture db,
        Guid tenant,
        string sub,
        params string[] permissions
    )
    {
        var profile = Guid.CreateVersion7();
        await using var session = await Session.BeginAsync(db.ConnectionString("app_core"), tenant);
        await session.ExecuteAsync(
            "INSERT INTO core.profiles (tenant_id, id, name, grants_all) VALUES ($1, $2, $3, false)",
            tenant,
            profile,
            $"Test {profile}"
        );
        foreach (var permission in permissions)
        {
            await session.ExecuteAsync(
                "INSERT INTO core.profile_permissions VALUES ($1, $2, $3)",
                tenant,
                profile,
                permission
            );
        }
        await AssignAsync(session, tenant, sub, profile);
        await session.CommitAsync();
        return profile;
    }

    /// <summary>A new establishment of <paramref name="tenant"/>. Returns its id.</summary>
    public static async Task<Guid> CreateEstablishmentAsync(
        DatabaseFixture db,
        Guid tenant,
        Jurisdiction jurisdiction = Jurisdiction.US
    )
    {
        var establishment = Guid.CreateVersion7();
        await using var session = await Session.BeginAsync(db.ConnectionString("app_core"), tenant);
        await session.ExecuteAsync(
            """
            INSERT INTO core.establishments (id, tenant_id, name, type, jurisdiction)
            VALUES ($1, $2, 'Kitchen', 'Restaurant', $3)
            """,
            establishment,
            tenant,
            jurisdiction.ToString()
        );
        await session.CommitAsync();
        return establishment;
    }

    /// <summary>
    /// A new tenant with its Owner and a US establishment, with one indexed document unless told
    /// otherwise. That document's id is the establishment's, so an inspection's completion can cite
    /// it as the establishment's evidence.
    /// </summary>
    public static async Task<Target> NewTargetAsync(DatabaseFixture db, bool indexed = true)
    {
        var owner = Guid.NewGuid().ToString();
        var tenant = await CreateAsync(db, null, owner);
        var target = new Target(tenant, await CreateEstablishmentAsync(db, tenant), owner);
        if (indexed)
        {
            await AddDocumentAsync(db, target, "Indexed", id: target.Establishment);
        }
        return target;
    }

    /// <summary>A document of the establishment whose one version has <paramref name="status"/>. Returns its id.</summary>
    public static async Task<Guid> AddDocumentAsync(
        DatabaseFixture db,
        Target target,
        string status,
        bool deleted = false,
        Guid? id = null
    )
    {
        var document = id ?? Guid.CreateVersion7();
        await using var session = await Session.BeginAsync(
            db.ConnectionString("app_core"),
            target.Tenant
        );
        await session.ExecuteAsync(
            """
            INSERT INTO core.documents (id, tenant_id, establishment_id, title, type, created_at, deleted_at)
            VALUES ($1, $2, $3, 'SOP', 'Sop', now(), CASE WHEN $4 THEN now() END)
            """,
            document,
            target.Tenant,
            target.Establishment,
            deleted
        );
        await session.ExecuteAsync(
            """
            INSERT INTO core.document_versions (id, tenant_id, document_id, number, file_name,
                content_type, size, sha256, blob_path, uploaded_by, uploaded_at, status, status_changed_at)
            SELECT $1, $2, $3, 1, 'sop.pdf', 'application/pdf', 1, '00', 'path', id, now(), $4, now()
            FROM core.users WHERE sub = $5
            """,
            Guid.CreateVersion7(),
            target.Tenant,
            document,
            status,
            target.Owner
        );
        await session.CommitAsync();
        return document;
    }

    private static async Task AssignAsync(Session session, Guid tenant, string sub, Guid profile)
    {
        await session.ExecuteAsync(
            """
            INSERT INTO core.users (tenant_id, id, sub) VALUES ($1, $2, $3)
            ON CONFLICT (tenant_id, sub) DO NOTHING
            """,
            tenant,
            Guid.CreateVersion7(),
            sub
        );
        await session.ExecuteAsync(
            "INSERT INTO core.user_profiles SELECT $1, id, $3 FROM core.users WHERE sub = $2",
            tenant,
            sub,
            profile
        );
    }
}
