using Normetry.Domain.Identity;
using Xunit;

namespace Normetry.IntegrationTests.Database;

/// <summary>What the migrations leave in the catalog: RLS everywhere app_core can reach, policies only for app_core, minimal grants.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class CatalogTests
{
    private readonly DatabaseFixture db;

    public CatalogTests(DatabaseFixture db)
    {
        db.SkipWithoutDocker();
        this.db = db;
    }

    private Task<Session> OwnerAsync() => Session.BeginAsync(db.ConnectionString("core_owner"));

    [Fact]
    public async Task Every_core_table_app_core_can_use_has_RLS_enabled_and_forced()
    {
        // The one exception is the global permission catalog, which app_core can only read. The
        // first migration revokes app_core's access to the history table.
        await using var session = await OwnerAsync();
        var unforced = await session.ColumnAsync<string>(
            """
            SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'core' AND c.relkind IN ('r', 'p', 'v', 'm', 'f')
              AND has_table_privilege('app_core', c.oid, 'SELECT, INSERT, UPDATE, DELETE, TRUNCATE')
              AND NOT (c.relrowsecurity AND c.relforcerowsecurity)
              AND c.relname <> 'permissions'
            """
        );
        var usable = await session.ColumnAsync<string>(
            """
            SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'core'
              AND has_table_privilege('app_core', c.oid, 'SELECT, INSERT, UPDATE, DELETE, TRUNCATE')
            ORDER BY c.relname
            """
        );

        Assert.Equal(
            [
                "chat_message_counts",
                "establishments",
                "permissions",
                "profile_permissions",
                "profiles",
                "tenants",
                "user_profiles",
                "users",
            ],
            usable
        ); // the check is not vacuous
        Assert.Empty(unforced);
    }

    [Fact]
    public async Task Every_core_policy_is_for_app_core_and_none_targets_PUBLIC()
    {
        await using var session = await OwnerAsync();
        var policies = await session.ColumnAsync<string>(
            """
            SELECT tablename || '.' || policyname || ' ' || cmd || ' ' || array_to_string(roles, ',')
            FROM pg_policies WHERE schemaname = 'core' ORDER BY 1
            """
        );

        Assert.Equal(
            [
                "chat_message_counts.chat_message_counts_tenant ALL app_core",
                "establishments.establishments_tenant ALL app_core",
                "profile_permissions.profile_permissions_tenant ALL app_core",
                "profiles.profiles_tenant ALL app_core",
                "tenants.tenants_insert INSERT app_core",
                "tenants.tenants_select SELECT app_core",
                "user_profiles.user_profiles_tenant ALL app_core",
                "users.users_tenant ALL app_core",
            ],
            policies
        );
        Assert.DoesNotContain(policies, policy => policy.Contains("public"));
    }

    [Fact]
    public async Task App_core_has_only_the_privileges_its_policies_need()
    {
        await using var session = await OwnerAsync();
        var grants = await session.ColumnAsync<string>(
            """
            SELECT c.relname || ' ' || a.grantee::regrole::text || ' ' || a.privilege_type
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace, aclexplode(c.relacl) a
            WHERE n.nspname = 'core' AND a.grantee <> c.relowner
            ORDER BY 1
            """
        );

        Assert.Equal(
            [
                "chat_message_counts app_core INSERT",
                "chat_message_counts app_core SELECT",
                "chat_message_counts app_core UPDATE",
                "establishments app_core DELETE",
                "establishments app_core INSERT",
                "establishments app_core SELECT",
                "establishments app_core UPDATE",
                "permissions app_core SELECT",
                "profile_permissions app_core DELETE",
                "profile_permissions app_core INSERT",
                "profile_permissions app_core SELECT",
                "profiles app_core DELETE",
                "profiles app_core INSERT",
                "profiles app_core SELECT",
                "profiles app_core UPDATE",
                "tenants app_core INSERT",
                "tenants app_core SELECT",
                "user_profiles app_core DELETE",
                "user_profiles app_core INSERT",
                "user_profiles app_core SELECT",
                "users app_core INSERT",
                "users app_core SELECT",
                "users app_core UPDATE",
            ],
            grants
        );
    }

    [Theory]
    [InlineData("""SELECT * FROM core."__EFMigrationsHistory" """)]
    [InlineData("""INSERT INTO core."__EFMigrationsHistory" VALUES ('x', 'x')""")]
    [InlineData("""UPDATE core."__EFMigrationsHistory" SET product_version = 'x'""")]
    [InlineData("""DELETE FROM core."__EFMigrationsHistory" """)]
    public async Task App_core_cannot_touch_the_migration_history(string sql)
    {
        await using var session = await Session.BeginAsync(db.ConnectionString("app_core"));

        await session.AssertDeniedAsync(sql);
    }

    [Fact]
    public async Task The_migration_history_lists_the_applied_migrations()
    {
        await using var session = await OwnerAsync();

        var applied = await session.ColumnAsync<string>(
            """SELECT migration_id FROM core."__EFMigrationsHistory" ORDER BY 1"""
        );

        Assert.Contains(applied, id => id.EndsWith("_CreateTenantsAndEstablishments"));
        Assert.Contains(applied, id => id.EndsWith("_CreateChatMessageCounts"));
        Assert.Contains(applied, id => id.EndsWith("_CreateUsersProfilesAndPermissions"));
        Assert.Contains(applied, id => id.EndsWith("_InsertPermissionCatalog"));
    }

    [Fact]
    public async Task The_permission_catalog_holds_the_names_in_code()
    {
        await using var session = await Session.BeginAsync(db.ConnectionString("app_core"));

        var names = await session.ColumnAsync<string>(
            "SELECT name FROM core.permissions ORDER BY name COLLATE \"C\""
        );

        Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), names);
    }

    [Theory]
    [InlineData("INSERT INTO core.permissions (name) VALUES ('reports.export')")]
    [InlineData("UPDATE core.permissions SET name = 'chat.shout' WHERE name = 'chat.ask'")]
    [InlineData("DELETE FROM core.permissions WHERE name = 'chat.ask'")]
    public async Task App_core_cannot_change_the_permission_catalog(string sql)
    {
        await using var session = await Session.BeginAsync(
            db.ConnectionString("app_core"),
            Guid.NewGuid()
        );

        await session.AssertDeniedAsync(sql);
    }
}
