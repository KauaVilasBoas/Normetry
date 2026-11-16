using Normetry.Domain.Identity;
using Xunit;

namespace Normetry.IntegrationTests.Database;

/// <summary>What the migrations leave in the catalog: RLS everywhere the runtime roles (app_core, core_dispatcher) can reach, policies only for them, minimal grants.</summary>
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
    public async Task Every_core_table_a_runtime_role_can_use_has_RLS_enabled_and_forced()
    {
        // The one exception is the global permission catalog, which app_core can only read. The
        // first migration revokes app_core's access to the history table.
        await using var session = await OwnerAsync();
        var unforced = await session.ColumnAsync<string>(
            """
            SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'core' AND c.relkind IN ('r', 'p', 'v', 'm', 'f')
              AND (has_table_privilege('app_core', c.oid, 'SELECT, INSERT, UPDATE, DELETE, TRUNCATE')
                OR has_table_privilege('core_dispatcher', c.oid, 'SELECT, INSERT, UPDATE, DELETE, TRUNCATE'))
              AND NOT (c.relrowsecurity AND c.relforcerowsecurity)
              AND c.relname <> 'permissions'
            """
        );
        var usable = await session.ColumnAsync<string>(
            """
            SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'core'
              AND (has_table_privilege('app_core', c.oid, 'SELECT, INSERT, UPDATE, DELETE, TRUNCATE')
                OR has_table_privilege('core_dispatcher', c.oid, 'SELECT, INSERT, UPDATE, DELETE, TRUNCATE'))
            ORDER BY c.relname
            """
        );

        Assert.Equal(
            [
                "chat_message_counts",
                "document_versions",
                "documents",
                "establishments",
                "inbox_messages",
                "outbox_messages",
                "outbox_tenants",
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
    public async Task Every_core_policy_is_for_a_runtime_role_and_none_targets_PUBLIC()
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
                "document_versions.document_versions_tenant ALL app_core",
                "documents.documents_tenant ALL app_core",
                "establishments.establishments_tenant ALL app_core",
                "inbox_messages.inbox_messages_tenant ALL app_core",
                "outbox_messages.outbox_messages_tenant ALL app_core",
                "outbox_tenants.outbox_tenants_dispatcher SELECT core_dispatcher",
                "outbox_tenants.outbox_tenants_insert INSERT app_core",
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
    public async Task The_runtime_roles_have_only_the_privileges_their_policies_need()
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
                "document_versions app_core INSERT",
                "document_versions app_core SELECT",
                "document_versions app_core UPDATE",
                "documents app_core INSERT",
                "documents app_core SELECT",
                "documents app_core UPDATE",
                "establishments app_core DELETE",
                "establishments app_core INSERT",
                "establishments app_core SELECT",
                "establishments app_core UPDATE",
                "inbox_messages app_core INSERT",
                "inbox_messages app_core SELECT",
                "outbox_messages app_core INSERT",
                "outbox_messages app_core SELECT",
                "outbox_messages app_core UPDATE",
                "outbox_tenants app_core INSERT",
                "outbox_tenants core_dispatcher SELECT",
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

    [Fact]
    public async Task Core_dispatcher_can_only_read_the_tenant_list()
    {
        // Every privilege on every relation in core, PUBLIC grants and memberships included, and
        // no way into schema ai.
        await using var session = await OwnerAsync();
        var privileges = await session.ColumnAsync<string>(
            """
            SELECT c.relname || ' ' || p.privilege
            FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace,
                 unnest(ARRAY['SELECT', 'INSERT', 'UPDATE', 'DELETE', 'TRUNCATE', 'REFERENCES',
                              'TRIGGER']) p(privilege)
            WHERE n.nspname = 'core' AND c.relkind IN ('r', 'p', 'v', 'm', 'f')
              AND has_table_privilege('core_dispatcher', c.oid, p.privilege)
            ORDER BY 1
            """
        );

        Assert.Equal(["outbox_tenants SELECT"], privileges);
        Assert.False(
            await session.ScalarAsync<bool>(
                "SELECT has_schema_privilege('core_dispatcher', 'core', 'CREATE')"
            )
        );
        Assert.False(
            await session.ScalarAsync<bool>(
                "SELECT has_schema_privilege('core_dispatcher', 'ai', 'USAGE')"
            )
        );
    }

    [Fact]
    public async Task A_function_core_owner_creates_is_executable_by_no_runtime_role()
    {
        // The bootstrap revokes PUBLIC's default EXECUTE on core_owner's functions. The probe is
        // rolled back with the session.
        await using var session = await OwnerAsync();
        await session.ExecuteAsync(
            "CREATE FUNCTION core.probe() RETURNS int LANGUAGE sql AS 'SELECT 1'"
        );
        var executable = await session.ColumnAsync<string>(
            """
            SELECT p.proname || ' ' || r.role
            FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace,
                 unnest(ARRAY['app_core', 'core_dispatcher', 'app_ai', 'corpus_ingestor']) r(role)
            WHERE n.nspname = 'core' AND has_function_privilege(r.role, p.oid, 'EXECUTE')
            ORDER BY 1
            """
        );

        Assert.Empty(executable);
        Assert.True(
            await session.ScalarAsync<bool>(
                "SELECT has_function_privilege('core_owner', 'core.probe()', 'EXECUTE')"
            )
        ); // the check is not vacuous
    }

    [Theory]
    [InlineData("SELECT * FROM core.outbox_messages")]
    [InlineData("SELECT * FROM core.inbox_messages")]
    [InlineData("SELECT * FROM core.documents")]
    [InlineData("SELECT * FROM core.document_versions")]
    [InlineData("SELECT * FROM core.users")]
    [InlineData("SELECT * FROM core.tenants")]
    [InlineData("SELECT * FROM core.establishments")]
    [InlineData("SELECT * FROM core.profiles")]
    [InlineData("SELECT * FROM core.permissions")]
    [InlineData("""SELECT * FROM core."__EFMigrationsHistory" """)]
    [InlineData(
        "INSERT INTO core.outbox_messages (tenant_id, id, type, routing_key, body, created_at) VALUES (gen_random_uuid(), gen_random_uuid(), 'x', 'x', '{}', now())"
    )]
    [InlineData("UPDATE core.outbox_messages SET sent_at = now()")]
    [InlineData("UPDATE core.documents SET title = 'x'")]
    [InlineData("DELETE FROM core.users")]
    [InlineData("INSERT INTO core.tenants (id, name) VALUES (gen_random_uuid(), 'x')")]
    [InlineData("CREATE TABLE core.dispatcher_table (id int)")]
    public async Task Core_dispatcher_cannot_read_or_write_any_other_table(string sql)
    {
        await using var session = await Session.BeginAsync(
            db.ConnectionString("core_dispatcher"),
            Guid.NewGuid()
        );

        await session.AssertDeniedAsync(sql);
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
        Assert.Contains(applied, id => id.EndsWith("_CreateOutboxAndInbox"));
        Assert.Contains(applied, id => id.EndsWith("_CreateDocuments"));
        Assert.Contains(applied, id => id.EndsWith("_RestrictTenantListToCoreDispatcher"));
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
