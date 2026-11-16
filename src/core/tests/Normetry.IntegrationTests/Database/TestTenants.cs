namespace Normetry.IntegrationTests.Database;

/// <summary>
/// Tenants and users a test makes for itself, written in SQL as app_core under RLS. Profiles get
/// exactly the permissions given: no implied reads, unlike the API.
/// </summary>
public static class TestTenants
{
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
