using Normetry.Infrastructure.Messaging;
using Xunit;

namespace Normetry.IntegrationTests.Database;

/// <summary>
/// RLS on core.tenants, core.establishments, core.chat_message_counts, the identity tables
/// (profiles, profile_permissions, users, user_profiles), the messaging tables
/// (outbox_messages, inbox_messages, outbox_tenants), the document tables (documents,
/// document_versions) and the inspection tables (inspections, findings, action_items,
/// inspection_counts), in plain SQL as app_core (the rls-table skill), plus core_dispatcher's
/// read of the tenant list.
/// Every test makes its own tenants, so tests never see each other's rows.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RowLevelSecurityTests
{
    private const string InsertTenant = "INSERT INTO core.tenants (id, name) VALUES ($1, 'Tenant')";
    private const string InsertEstablishment = """
        INSERT INTO core.establishments (id, tenant_id, name, type, jurisdiction)
        VALUES ($1, $2, 'Kitchen', 'Restaurant', 'US')
        """;
    private const string VisibleEstablishments =
        "SELECT id FROM core.establishments WHERE id = ANY($1)";
    private const string InsertChatCount = """
        INSERT INTO core.chat_message_counts (tenant_id, user_id, day, count)
        VALUES ($1, 'user', DATE '2031-01-01', 1)
        """;
    private const string VisibleChatCounts =
        "SELECT tenant_id FROM core.chat_message_counts WHERE tenant_id = ANY($1)";

    private readonly DatabaseFixture db;

    public RowLevelSecurityTests(DatabaseFixture db)
    {
        db.SkipWithoutDocker();
        this.db = db;
    }

    private string AppCore => db.ConnectionString("app_core");

    /// <summary>Two new tenants with one establishment each, written by app_core.</summary>
    private async Task<(
        Guid TenantA,
        Guid TenantB,
        Guid EstablishmentA,
        Guid EstablishmentB
    )> SeedAsync()
    {
        var (tenantA, tenantB) = (Guid.NewGuid(), Guid.NewGuid());
        var (establishmentA, establishmentB) = (Guid.CreateVersion7(), Guid.CreateVersion7());
        foreach (
            var (tenant, establishment) in new[]
            {
                (tenantA, establishmentA),
                (tenantB, establishmentB),
            }
        )
        {
            await using var session = await Session.BeginAsync(AppCore, tenant);
            await session.ExecuteAsync(InsertTenant, tenant);
            await session.ExecuteAsync(InsertEstablishment, establishment, tenant);
            await session.CommitAsync();
        }
        return (tenantA, tenantB, establishmentA, establishmentB);
    }

    [Fact]
    public async Task Each_tenant_reads_only_its_own_establishments()
    {
        var (tenantA, tenantB, establishmentA, establishmentB) = await SeedAsync();
        Guid[] both = [establishmentA, establishmentB];

        await using (var session = await Session.BeginAsync(AppCore, tenantA))
        {
            Assert.Equal(
                [establishmentA],
                await session.ColumnAsync<Guid>(VisibleEstablishments, both)
            );
        }
        await using (var session = await Session.BeginAsync(AppCore, tenantB))
        {
            Assert.Equal(
                [establishmentB],
                await session.ColumnAsync<Guid>(VisibleEstablishments, both)
            );
        }
    }

    [Fact]
    public async Task A_tenant_cannot_update_or_delete_another_tenants_establishments()
    {
        var (tenantA, tenantB, establishmentA, _) = await SeedAsync();

        await using (var session = await Session.BeginAsync(AppCore, tenantB))
        {
            var updated = await session.ExecuteAsync(
                "UPDATE core.establishments SET name = 'Taken' WHERE id = $1",
                establishmentA
            );
            var deleted = await session.ExecuteAsync(
                "DELETE FROM core.establishments WHERE id = $1",
                establishmentA
            );
            Assert.Equal((0, 0), (updated, deleted));
            await session.CommitAsync();
        }

        await using (var session = await Session.BeginAsync(AppCore, tenantA))
        {
            var name = await session.ScalarAsync<string>(
                "SELECT name FROM core.establishments WHERE id = $1",
                establishmentA
            );
            Assert.Equal("Kitchen", name);
        }
    }

    [Fact]
    public async Task A_tenant_cannot_insert_or_move_rows_into_another_tenant()
    {
        var (tenantA, tenantB, establishmentA, _) = await SeedAsync();

        await using (var session = await Session.BeginAsync(AppCore, tenantA))
        {
            await session.AssertDeniedAsync(InsertEstablishment, Guid.CreateVersion7(), tenantB);
        }
        await using (var session = await Session.BeginAsync(AppCore, tenantA))
        {
            await session.AssertDeniedAsync(
                "UPDATE core.establishments SET tenant_id = $1 WHERE id = $2",
                tenantB,
                establishmentA
            );
        }
    }

    [Fact]
    public async Task Without_a_tenant_no_establishment_is_visible_and_writes_fail()
    {
        var (tenantA, _, establishmentA, establishmentB) = await SeedAsync();

        await using (var session = await Session.BeginAsync(AppCore))
        {
            Assert.Equal(
                0L,
                await session.ScalarAsync<long>("SELECT count(*) FROM core.establishments")
            );
            Assert.Equal(
                0,
                await session.ExecuteAsync("UPDATE core.establishments SET name = 'x'")
            );
            Assert.Equal(0, await session.ExecuteAsync("DELETE FROM core.establishments"));
            await session.CommitAsync();
        }
        await using (var session = await Session.BeginAsync(AppCore))
        {
            await session.AssertDeniedAsync(InsertEstablishment, Guid.CreateVersion7(), tenantA);
        }

        await using var tenantSession = await Session.BeginAsync(AppCore, tenantA);
        Assert.Equal(
            [establishmentA],
            await tenantSession.ColumnAsync<Guid>(
                VisibleEstablishments,
                new[] { establishmentA, establishmentB }
            )
        );
    }

    [Fact]
    public async Task A_session_sees_only_its_own_tenant_row()
    {
        var (tenantA, tenantB, _, _) = await SeedAsync();
        const string Visible = "SELECT id FROM core.tenants WHERE id = ANY($1)";
        Guid[] both = [tenantA, tenantB];

        await using (var session = await Session.BeginAsync(AppCore, tenantA))
        {
            Assert.Equal([tenantA], await session.ColumnAsync<Guid>(Visible, both));
        }
        await using (var session = await Session.BeginAsync(AppCore))
        {
            Assert.Equal(0L, await session.ScalarAsync<long>("SELECT count(*) FROM core.tenants"));
        }
    }

    [Theory]
    [InlineData("another tenant", InsertTenant)]
    [InlineData("no tenant", InsertTenant)]
    [InlineData("update", "UPDATE core.tenants SET name = 'Renamed' WHERE id = $1")]
    [InlineData("delete", "DELETE FROM core.tenants WHERE id = $1")]
    public async Task App_core_cannot_create_other_tenants_or_change_any_tenant(
        string attempt,
        string sql
    )
    {
        var (tenantA, _, _, _) = await SeedAsync();
        // Inserts use a new id; update and delete target the session's own tenant row.
        var (sessionTenant, target) = attempt switch
        {
            "another tenant" => ((Guid?)tenantA, Guid.NewGuid()),
            "no tenant" => (null, Guid.NewGuid()),
            _ => (tenantA, tenantA),
        };

        await using var session = await Session.BeginAsync(AppCore, sessionTenant);
        await session.AssertDeniedAsync(sql, target);
    }

    /// <summary>Two new tenants with one chat count each, written by app_core.</summary>
    private async Task<(Guid TenantA, Guid TenantB)> SeedChatCountsAsync()
    {
        var (tenantA, tenantB, _, _) = await SeedAsync();
        foreach (var tenant in new[] { tenantA, tenantB })
        {
            await using var session = await Session.BeginAsync(AppCore, tenant);
            await session.ExecuteAsync(InsertChatCount, tenant);
            await session.CommitAsync();
        }
        return (tenantA, tenantB);
    }

    [Fact]
    public async Task Each_tenant_reads_and_updates_only_its_own_chat_counts()
    {
        var (tenantA, tenantB) = await SeedChatCountsAsync();
        Guid[] both = [tenantA, tenantB];

        await using (var session = await Session.BeginAsync(AppCore, tenantB))
        {
            Assert.Equal([tenantB], await session.ColumnAsync<Guid>(VisibleChatCounts, both));
            var updated = await session.ExecuteAsync(
                "UPDATE core.chat_message_counts SET count = 99 WHERE tenant_id = $1",
                tenantA
            );
            Assert.Equal(0, updated);
            await session.CommitAsync();
        }

        await using var sessionA = await Session.BeginAsync(AppCore, tenantA);
        Assert.Equal([tenantA], await sessionA.ColumnAsync<Guid>(VisibleChatCounts, both));
        Assert.Equal(
            1,
            await sessionA.ScalarAsync<int>(
                "SELECT count FROM core.chat_message_counts WHERE tenant_id = $1",
                tenantA
            )
        );
    }

    [Fact]
    public async Task App_core_cannot_delete_chat_counts_even_its_own()
    {
        var (tenantA, _) = await SeedChatCountsAsync();

        await using var session = await Session.BeginAsync(AppCore, tenantA);
        await session.AssertDeniedAsync(
            "DELETE FROM core.chat_message_counts WHERE tenant_id = $1",
            tenantA
        );
    }

    [Fact]
    public async Task A_tenant_cannot_insert_or_move_chat_counts_into_another_tenant()
    {
        var (tenantA, tenantB) = await SeedChatCountsAsync();

        await using (var session = await Session.BeginAsync(AppCore, tenantB))
        {
            await session.AssertDeniedAsync(
                InsertChatCount.Replace("'user'", "'another user'"),
                tenantA
            );
        }
        await using (var session = await Session.BeginAsync(AppCore, tenantA))
        {
            await session.AssertDeniedAsync(
                "UPDATE core.chat_message_counts SET tenant_id = $1 WHERE tenant_id = $2",
                tenantB,
                tenantA
            );
        }
    }

    [Fact]
    public async Task Without_a_tenant_no_chat_count_is_visible_and_writes_fail()
    {
        var (tenantA, tenantB) = await SeedChatCountsAsync();

        await using (var session = await Session.BeginAsync(AppCore))
        {
            Assert.Empty(
                await session.ColumnAsync<Guid>(VisibleChatCounts, new[] { tenantA, tenantB })
            );
            Assert.Equal(
                0,
                await session.ExecuteAsync("UPDATE core.chat_message_counts SET count = 99")
            );
        }
        await using (var session = await Session.BeginAsync(AppCore))
        {
            await session.AssertDeniedAsync(
                InsertChatCount.Replace("'user'", "'another user'"),
                tenantA
            );
        }
    }

    /// <summary>
    /// The identity rows of one tenant: a profile granting chat.ask held by a user, and a second
    /// profile nobody holds.
    /// </summary>
    private sealed record Identity(Guid Tenant, Guid Profile, Guid SpareProfile, Guid User);

    private static readonly string[] IdentityTables =
    [
        "profiles",
        "profile_permissions",
        "users",
        "user_profiles",
    ];

    /// <summary>Two new tenants with their identity rows, written by app_core.</summary>
    private async Task<(Identity A, Identity B)> SeedIdentityAsync()
    {
        var (tenantA, tenantB, _, _) = await SeedAsync();
        var identities = new List<Identity>();
        foreach (var tenant in new[] { tenantA, tenantB })
        {
            var identity = new Identity(
                tenant,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7()
            );
            await using var session = await Session.BeginAsync(AppCore, tenant);
            await session.ExecuteAsync(
                """
                INSERT INTO core.profiles (tenant_id, id, name, grants_all)
                VALUES ($1, $2, 'Kitchen', false), ($1, $3, 'Spare', false)
                """,
                tenant,
                identity.Profile,
                identity.SpareProfile
            );
            await session.ExecuteAsync(
                "INSERT INTO core.profile_permissions VALUES ($1, $2, 'chat.ask')",
                tenant,
                identity.Profile
            );
            await session.ExecuteAsync(
                "INSERT INTO core.users (tenant_id, id, sub) VALUES ($1, $2, 'user')",
                tenant,
                identity.User
            );
            await session.ExecuteAsync(
                "INSERT INTO core.user_profiles VALUES ($1, $2, $3)",
                tenant,
                identity.User,
                identity.Profile
            );
            await session.CommitAsync();
            identities.Add(identity);
        }
        return (identities[0], identities[1]);
    }

    /// <summary>A new row for <paramref name="table"/> in <paramref name="target"/>'s tenant.</summary>
    private static (string Sql, object[] Parameters) InsertIdentity(
        string table,
        Identity target
    ) =>
        table switch
        {
            "profiles" => (
                "INSERT INTO core.profiles (tenant_id, id, name, grants_all) VALUES ($1, $2, 'New', false)",
                [target.Tenant, Guid.CreateVersion7()]
            ),
            "profile_permissions" => (
                "INSERT INTO core.profile_permissions VALUES ($1, $2, 'profiles.read')",
                [target.Tenant, target.Profile]
            ),
            "users" => (
                "INSERT INTO core.users (tenant_id, id, sub) VALUES ($1, $2, 'new')",
                [target.Tenant, Guid.CreateVersion7()]
            ),
            _ => (
                "INSERT INTO core.user_profiles VALUES ($1, $2, $3)",
                [target.Tenant, target.User, target.SpareProfile]
            ),
        };

    [Theory]
    [InlineData("profiles")]
    [InlineData("profile_permissions")]
    [InlineData("users")]
    [InlineData("user_profiles")]
    public async Task Each_tenant_reads_only_its_own_identity_rows(string table)
    {
        var (a, b) = await SeedIdentityAsync();
        var visible = $"SELECT DISTINCT tenant_id FROM core.{table} WHERE tenant_id = ANY($1)";
        Guid[] both = [a.Tenant, b.Tenant];

        await using (var session = await Session.BeginAsync(AppCore, a.Tenant))
        {
            Assert.Equal([a.Tenant], await session.ColumnAsync<Guid>(visible, both));
        }
        await using (var session = await Session.BeginAsync(AppCore, b.Tenant))
        {
            Assert.Equal([b.Tenant], await session.ColumnAsync<Guid>(visible, both));
        }
    }

    [Theory]
    [InlineData("UPDATE core.profiles SET name = 'Taken' WHERE tenant_id = $1")]
    [InlineData("UPDATE core.users SET display_name = 'Taken' WHERE tenant_id = $1")]
    [InlineData("DELETE FROM core.profiles WHERE tenant_id = $1")]
    [InlineData("DELETE FROM core.profile_permissions WHERE tenant_id = $1")]
    [InlineData("DELETE FROM core.user_profiles WHERE tenant_id = $1")]
    public async Task A_tenant_cannot_update_or_delete_another_tenants_identity_rows(string sql)
    {
        var (a, b) = await SeedIdentityAsync();

        await using (var session = await Session.BeginAsync(AppCore, b.Tenant))
        {
            Assert.Equal(0, await session.ExecuteAsync(sql, a.Tenant));
            await session.CommitAsync();
        }

        await using var sessionA = await Session.BeginAsync(AppCore, a.Tenant);
        Assert.Equal(
            ("Kitchen", (string?)null, 1L, 1L),
            (
                await sessionA.ScalarAsync<string>(
                    "SELECT name FROM core.profiles WHERE id = $1",
                    a.Profile
                ),
                await sessionA.ScalarAsync<object>(
                    "SELECT display_name FROM core.users WHERE id = $1",
                    a.User
                ) as string,
                await sessionA.ScalarAsync<long>("SELECT count(*) FROM core.profile_permissions"),
                await sessionA.ScalarAsync<long>("SELECT count(*) FROM core.user_profiles")
            )
        );
    }

    [Theory]
    [InlineData("profiles")]
    [InlineData("profile_permissions")]
    [InlineData("users")]
    [InlineData("user_profiles")]
    public async Task A_tenant_cannot_insert_identity_rows_into_another_tenant(string table)
    {
        var (a, b) = await SeedIdentityAsync();
        var (sql, parameters) = InsertIdentity(table, b);

        await using var session = await Session.BeginAsync(AppCore, a.Tenant);
        await session.AssertDeniedAsync(sql, parameters);
    }

    [Theory]
    [InlineData("profiles")]
    [InlineData("profile_permissions")]
    [InlineData("users")]
    [InlineData("user_profiles")]
    public async Task Without_a_tenant_no_identity_row_is_visible_and_inserts_fail(string table)
    {
        var (a, b) = await SeedIdentityAsync();
        var (sql, parameters) = InsertIdentity(table, a);

        await using (var session = await Session.BeginAsync(AppCore))
        {
            Assert.Empty(
                await session.ColumnAsync<Guid>(
                    $"SELECT tenant_id FROM core.{table} WHERE tenant_id = ANY($1)",
                    new[] { a.Tenant, b.Tenant }
                )
            );
        }
        await using (var session = await Session.BeginAsync(AppCore))
        {
            await session.AssertDeniedAsync(sql, parameters);
        }
    }

    [Theory]
    [InlineData("profiles")]
    [InlineData("users")]
    public async Task A_tenant_cannot_move_identity_rows_into_another_tenant(string table)
    {
        var (a, b) = await SeedIdentityAsync();

        await using var session = await Session.BeginAsync(AppCore, a.Tenant);
        await session.AssertDeniedAsync(
            $"UPDATE core.{table} SET tenant_id = $1 WHERE tenant_id = $2",
            b.Tenant,
            a.Tenant
        );
    }

    [Fact]
    public async Task Deleting_a_profile_never_reaches_another_tenants_rows_with_the_same_id()
    {
        // The key is (tenant_id, id), so two tenants may hold the same profile id. The cascade
        // runs without RLS; the composite foreign keys keep it inside one tenant.
        var (a, b) = await SeedIdentityAsync();
        var shared = Guid.CreateVersion7();
        foreach (var identity in new[] { a, b })
        {
            await using var session = await Session.BeginAsync(AppCore, identity.Tenant);
            await session.ExecuteAsync(
                "INSERT INTO core.profiles (tenant_id, id, name, grants_all) VALUES ($1, $2, 'Shared', false)",
                identity.Tenant,
                shared
            );
            await session.ExecuteAsync(
                "INSERT INTO core.profile_permissions VALUES ($1, $2, 'chat.ask')",
                identity.Tenant,
                shared
            );
            await session.ExecuteAsync(
                "INSERT INTO core.user_profiles VALUES ($1, $2, $3)",
                identity.Tenant,
                identity.User,
                shared
            );
            await session.CommitAsync();
        }

        await using (var session = await Session.BeginAsync(AppCore, a.Tenant))
        {
            Assert.Equal(
                1,
                await session.ExecuteAsync("DELETE FROM core.profiles WHERE id = $1", shared)
            );
            await session.CommitAsync();
        }

        await using var sessionB = await Session.BeginAsync(AppCore, b.Tenant);
        Assert.Equal(
            (1L, 1L),
            (
                await sessionB.ScalarAsync<long>(
                    "SELECT count(*) FROM core.profile_permissions WHERE profile_id = $1",
                    shared
                ),
                await sessionB.ScalarAsync<long>(
                    "SELECT count(*) FROM core.user_profiles WHERE profile_id = $1",
                    shared
                )
            )
        );
    }

    [Theory]
    [InlineData("profiles")]
    [InlineData("profile_permissions")]
    [InlineData("users")]
    [InlineData("user_profiles")]
    public async Task The_owner_sees_and_writes_no_identity_rows_even_with_a_tenant_set(
        string table
    )
    {
        // FORCE ROW LEVEL SECURITY with no policy for core_owner.
        var (a, _) = await SeedIdentityAsync();
        var owner = db.ConnectionString("core_owner");
        var (sql, parameters) = InsertIdentity(table, a);

        await using (var session = await Session.BeginAsync(owner, a.Tenant))
        {
            Assert.Equal(0L, await session.ScalarAsync<long>($"SELECT count(*) FROM core.{table}"));
        }
        await using (var session = await Session.BeginAsync(owner, a.Tenant))
        {
            await session.AssertDeniedAsync(sql, parameters);
        }
    }

    [Fact]
    public async Task A_link_cannot_join_rows_of_two_tenants()
    {
        // Row-level security accepts these rows (their tenant is the session's), but the composite
        // foreign keys find no profile with that id in this tenant.
        var (a, b) = await SeedIdentityAsync();

        foreach (
            var (sql, parameters) in new (string, object[])[]
            {
                (
                    "INSERT INTO core.user_profiles VALUES ($1, $2, $3)",
                    [a.Tenant, a.User, b.Profile]
                ),
                (
                    "INSERT INTO core.profile_permissions VALUES ($1, $2, 'profiles.read')",
                    [a.Tenant, b.Profile]
                ),
            }
        )
        {
            await using var session = await Session.BeginAsync(AppCore, a.Tenant);
            var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
                session.ExecuteAsync(sql, parameters)
            );
            Assert.Equal("23503", error.SqlState); // foreign_key_violation
        }
    }

    [Fact]
    public async Task The_owner_sees_and_writes_no_rows_even_with_a_tenant_set()
    {
        // FORCE ROW LEVEL SECURITY with no policy for core_owner.
        var (tenantA, _, establishmentA, _) = await SeedAsync();
        var owner = db.ConnectionString("core_owner");

        await using (var session = await Session.BeginAsync(owner, tenantA))
        {
            Assert.Empty(
                await session.ColumnAsync<Guid>(VisibleEstablishments, new[] { establishmentA })
            );
            Assert.Equal(0L, await session.ScalarAsync<long>("SELECT count(*) FROM core.tenants"));
        }
        await using (var session = await Session.BeginAsync(owner, tenantA))
        {
            await session.AssertDeniedAsync(InsertEstablishment, Guid.CreateVersion7(), tenantA);
        }
    }

    /// <summary>A new row for <paramref name="table"/> (outbox_messages or inbox_messages) in <paramref name="tenant"/>.</summary>
    private static (string Sql, object[] Parameters) InsertMessage(string table, Guid tenant) =>
        table == "outbox_messages"
            ? (
                """
                INSERT INTO core.outbox_messages (tenant_id, id, type, routing_key, body, created_at)
                VALUES ($1, $2, 'DocumentDeleted', 'document.deleted.v1', '{}', now())
                """,
                [tenant, Guid.CreateVersion7()]
            )
            : (
                """
                INSERT INTO core.inbox_messages (tenant_id, message_id, type, received_at)
                VALUES ($1, $2, 'DocumentIndexed', now())
                """,
                [tenant, Guid.NewGuid()]
            );

    /// <summary>Two new tenants with one row each in <paramref name="table"/>, written by app_core.</summary>
    private async Task<(Guid TenantA, Guid TenantB)> SeedMessagesAsync(string table)
    {
        var (tenantA, tenantB, _, _) = await SeedAsync();
        foreach (var tenant in new[] { tenantA, tenantB })
        {
            await using var session = await Session.BeginAsync(AppCore, tenant);
            var (sql, parameters) = InsertMessage(table, tenant);
            await session.ExecuteAsync(sql, parameters);
            await session.CommitAsync();
        }
        return (tenantA, tenantB);
    }

    [Theory]
    [InlineData("outbox_messages")]
    [InlineData("inbox_messages")]
    public async Task Each_tenant_reads_only_its_own_messages(string table)
    {
        var (tenantA, tenantB) = await SeedMessagesAsync(table);
        var visible = $"SELECT DISTINCT tenant_id FROM core.{table} WHERE tenant_id = ANY($1)";
        Guid[] both = [tenantA, tenantB];

        await using (var session = await Session.BeginAsync(AppCore, tenantA))
        {
            Assert.Equal([tenantA], await session.ColumnAsync<Guid>(visible, both));
        }
        await using (var session = await Session.BeginAsync(AppCore, tenantB))
        {
            Assert.Equal([tenantB], await session.ColumnAsync<Guid>(visible, both));
        }
    }

    [Fact]
    public async Task A_tenant_cannot_mark_another_tenants_outbox_messages_sent()
    {
        var (tenantA, tenantB) = await SeedMessagesAsync("outbox_messages");

        await using (var session = await Session.BeginAsync(AppCore, tenantB))
        {
            Assert.Equal(
                0,
                await session.ExecuteAsync(
                    "UPDATE core.outbox_messages SET sent_at = now() WHERE tenant_id = $1",
                    tenantA
                )
            );
            await session.CommitAsync();
        }

        await using var sessionA = await Session.BeginAsync(AppCore, tenantA);
        Assert.Equal(
            1L,
            await sessionA.ScalarAsync<long>(
                "SELECT count(*) FROM core.outbox_messages WHERE sent_at IS NULL"
            )
        );
    }

    [Theory]
    [InlineData("DELETE FROM core.outbox_messages WHERE tenant_id = $1")]
    [InlineData("UPDATE core.inbox_messages SET type = 'x' WHERE tenant_id = $1")]
    [InlineData("DELETE FROM core.inbox_messages WHERE tenant_id = $1")]
    public async Task App_core_cannot_delete_outbox_rows_or_change_inbox_rows_even_its_own(
        string sql
    )
    {
        var (tenantA, _) = await SeedMessagesAsync(
            sql.Contains("outbox") ? "outbox_messages" : "inbox_messages"
        );

        await using var session = await Session.BeginAsync(AppCore, tenantA);
        await session.AssertDeniedAsync(sql, tenantA);
    }

    [Theory]
    [InlineData("outbox_messages")]
    [InlineData("inbox_messages")]
    public async Task A_tenant_cannot_insert_or_move_messages_into_another_tenant(string table)
    {
        var (tenantA, tenantB) = await SeedMessagesAsync(table);
        var (sql, parameters) = InsertMessage(table, tenantB);

        await using (var session = await Session.BeginAsync(AppCore, tenantA))
        {
            await session.AssertDeniedAsync(sql, parameters);
        }
        if (table == "outbox_messages")
        {
            await using var session = await Session.BeginAsync(AppCore, tenantA);
            await session.AssertDeniedAsync(
                "UPDATE core.outbox_messages SET tenant_id = $1 WHERE tenant_id = $2",
                tenantB,
                tenantA
            );
        }
    }

    [Theory]
    [InlineData("outbox_messages")]
    [InlineData("inbox_messages")]
    public async Task Without_a_tenant_no_message_is_visible_and_inserts_fail(string table)
    {
        var (tenantA, tenantB) = await SeedMessagesAsync(table);
        var (sql, parameters) = InsertMessage(table, tenantA);

        await using (var session = await Session.BeginAsync(AppCore))
        {
            Assert.Empty(
                await session.ColumnAsync<Guid>(
                    $"SELECT tenant_id FROM core.{table} WHERE tenant_id = ANY($1)",
                    new[] { tenantA, tenantB }
                )
            );
        }
        await using (var session = await Session.BeginAsync(AppCore))
        {
            await session.AssertDeniedAsync(sql, parameters);
        }
    }

    private const string InsertOutboxTenant =
        "INSERT INTO core.outbox_tenants (tenant_id) VALUES ($1)";
    private const string VisibleOutboxTenants =
        "SELECT tenant_id FROM core.outbox_tenants WHERE tenant_id = ANY($1)";

    /// <summary>Two new tenants, each listed in core.outbox_tenants by its own app_core session.</summary>
    private async Task<(Guid TenantA, Guid TenantB)> SeedOutboxTenantsAsync()
    {
        var (tenantA, tenantB, _, _) = await SeedAsync();
        foreach (var tenant in new[] { tenantA, tenantB })
        {
            await using var session = await Session.BeginAsync(AppCore, tenant);
            await session.ExecuteAsync(InsertOutboxTenant, tenant);
            await session.CommitAsync();
        }
        return (tenantA, tenantB);
    }

    [Fact]
    public async Task App_core_lists_only_its_own_tenant_and_never_reads_the_tenant_list()
    {
        var (tenantA, tenantB) = await SeedOutboxTenantsAsync();

        // The outbox's own statement: listing the tenant again is a no-op, without reading. With
        // another tenant's listed id it fails rather than answering 0, so it never reveals whether
        // a guessed tenant exists.
        const string ListTenant =
            "INSERT INTO core.outbox_tenants (tenant_id) VALUES ($1) ON CONFLICT DO NOTHING";
        await using (var session = await Session.BeginAsync(AppCore, tenantA))
        {
            Assert.Equal(0, await session.ExecuteAsync(ListTenant, tenantA));
        }
        await using (var session = await Session.BeginAsync(AppCore, tenantA))
        {
            await session.AssertDeniedAsync(ListTenant, tenantB);
        }
        await using (var session = await Session.BeginAsync(AppCore, tenantA))
        {
            await session.AssertDeniedAsync(InsertOutboxTenant, Guid.NewGuid());
        }
        await using (var session = await Session.BeginAsync(AppCore))
        {
            await session.AssertDeniedAsync(InsertOutboxTenant, Guid.NewGuid());
        }
        foreach (
            var sql in new[]
            {
                "UPDATE core.outbox_tenants SET tenant_id = tenant_id WHERE tenant_id = $1",
                "DELETE FROM core.outbox_tenants WHERE tenant_id = $1",
            }
        )
        {
            await using var session = await Session.BeginAsync(AppCore, tenantA);
            await session.AssertDeniedAsync(sql, tenantA);
        }
        // No app_core session reads the list, so a request path cannot learn another tenant's id:
        // not with another tenant set, not with its own, not with none.
        foreach (var tenant in new Guid?[] { tenantB, tenantA, null })
        {
            await using var session = await Session.BeginAsync(AppCore, tenant);
            await session.AssertDeniedAsync(VisibleOutboxTenants, new[] { tenantA, tenantB });
        }
    }

    [Fact]
    public async Task Core_dispatcher_reads_every_listed_tenant_and_writes_none()
    {
        // The one cross-tenant read: the tenants the outbox dispatcher and the sweeper visit. The
        // rows hold nothing but the id; every message is read as app_core under its tenant's RLS.
        var (tenantA, tenantB) = await SeedOutboxTenantsAsync();
        var dispatcher = db.ConnectionString("core_dispatcher");

        Assert.Superset(
            new HashSet<Guid> { tenantA, tenantB },
            (
                await OutboxTenantList.ListAsync(dispatcher, TestContext.Current.CancellationToken)
            ).ToHashSet()
        );
        foreach (
            var sql in new[]
            {
                InsertOutboxTenant,
                "UPDATE core.outbox_tenants SET tenant_id = tenant_id WHERE tenant_id = $1",
                "DELETE FROM core.outbox_tenants WHERE tenant_id = $1",
            }
        )
        {
            await using var session = await Session.BeginAsync(dispatcher);
            await session.AssertDeniedAsync(sql, tenantA);
        }
    }

    /// <summary>The document rows of one tenant: a user, a document of its establishment and one version.</summary>
    private sealed record Documents(
        Guid Tenant,
        Guid Establishment,
        Guid User,
        Guid Document,
        Guid Version
    );

    private const string InsertDocument = """
        INSERT INTO core.documents (id, tenant_id, establishment_id, title, type, created_at)
        VALUES ($1, $2, $3, 'SOP', 'Sop', now())
        """;
    private const string InsertVersion = """
        INSERT INTO core.document_versions (id, tenant_id, document_id, number, file_name,
            content_type, size, sha256, blob_path, uploaded_by, uploaded_at, status, status_changed_at)
        VALUES ($1, $2, $3, $4, 'sop.pdf', 'application/pdf', 1, '00', 'path', $5, now(), 'Uploaded', now())
        """;

    /// <summary>Two new tenants with their document rows, written by app_core.</summary>
    private async Task<(Documents A, Documents B)> SeedDocumentsAsync()
    {
        var (tenantA, tenantB, establishmentA, establishmentB) = await SeedAsync();
        var seeded = new List<Documents>();
        foreach (
            var (tenant, establishment) in new[]
            {
                (tenantA, establishmentA),
                (tenantB, establishmentB),
            }
        )
        {
            var rows = new Documents(
                tenant,
                establishment,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7()
            );
            await using var session = await Session.BeginAsync(AppCore, tenant);
            await session.ExecuteAsync(
                "INSERT INTO core.users (tenant_id, id, sub) VALUES ($1, $2, 'user')",
                tenant,
                rows.User
            );
            await session.ExecuteAsync(InsertDocument, rows.Document, tenant, establishment);
            await session.ExecuteAsync(
                InsertVersion,
                rows.Version,
                tenant,
                rows.Document,
                1,
                rows.User
            );
            await session.CommitAsync();
            seeded.Add(rows);
        }
        return (seeded[0], seeded[1]);
    }

    /// <summary>A new row for <paramref name="table"/> carrying <paramref name="target"/>'s tenant and ids.</summary>
    private static (string Sql, object[] Parameters) InsertDocumentRow(
        string table,
        Documents target
    ) =>
        table == "documents"
            ? (InsertDocument, [Guid.CreateVersion7(), target.Tenant, target.Establishment])
            : (
                InsertVersion,
                [Guid.CreateVersion7(), target.Tenant, target.Document, 2, target.User]
            );

    [Theory]
    [InlineData("documents")]
    [InlineData("document_versions")]
    public async Task Each_tenant_reads_only_its_own_documents(string table)
    {
        var (a, b) = await SeedDocumentsAsync();
        var visible = $"SELECT DISTINCT tenant_id FROM core.{table} WHERE tenant_id = ANY($1)";
        Guid[] both = [a.Tenant, b.Tenant];

        await using (var session = await Session.BeginAsync(AppCore, a.Tenant))
        {
            Assert.Equal([a.Tenant], await session.ColumnAsync<Guid>(visible, both));
        }
        await using (var session = await Session.BeginAsync(AppCore, b.Tenant))
        {
            Assert.Equal([b.Tenant], await session.ColumnAsync<Guid>(visible, both));
        }
    }

    [Theory]
    [InlineData("UPDATE core.documents SET deleted_at = now() WHERE tenant_id = $1")]
    [InlineData("UPDATE core.document_versions SET status = 'Indexed' WHERE tenant_id = $1")]
    public async Task A_tenant_cannot_update_another_tenants_documents(string sql)
    {
        var (a, b) = await SeedDocumentsAsync();

        await using (var session = await Session.BeginAsync(AppCore, b.Tenant))
        {
            Assert.Equal(0, await session.ExecuteAsync(sql, a.Tenant));
            await session.CommitAsync();
        }

        await using var sessionA = await Session.BeginAsync(AppCore, a.Tenant);
        Assert.Equal(
            ("Uploaded", 0L),
            (
                await sessionA.ScalarAsync<string>(
                    "SELECT status FROM core.document_versions WHERE id = $1",
                    a.Version
                ),
                await sessionA.ScalarAsync<long>(
                    "SELECT count(*) FROM core.documents WHERE deleted_at IS NOT NULL"
                )
            )
        );
    }

    [Theory]
    [InlineData("DELETE FROM core.documents WHERE tenant_id = $1")]
    [InlineData("DELETE FROM core.document_versions WHERE tenant_id = $1")]
    public async Task App_core_cannot_delete_documents_even_its_own(string sql)
    {
        var (a, _) = await SeedDocumentsAsync();

        await using var session = await Session.BeginAsync(AppCore, a.Tenant);
        await session.AssertDeniedAsync(sql, a.Tenant);
    }

    [Theory]
    [InlineData("documents")]
    [InlineData("document_versions")]
    public async Task A_tenant_cannot_insert_or_move_documents_into_another_tenant(string table)
    {
        var (a, b) = await SeedDocumentsAsync();
        var (sql, parameters) = InsertDocumentRow(table, b);

        await using (var session = await Session.BeginAsync(AppCore, a.Tenant))
        {
            await session.AssertDeniedAsync(sql, parameters);
        }
        await using (var session = await Session.BeginAsync(AppCore, a.Tenant))
        {
            await session.AssertDeniedAsync(
                $"UPDATE core.{table} SET tenant_id = $1 WHERE tenant_id = $2",
                b.Tenant,
                a.Tenant
            );
        }
    }

    [Theory]
    [InlineData("documents")]
    [InlineData("document_versions")]
    public async Task Without_a_tenant_no_document_is_visible_and_inserts_fail(string table)
    {
        var (a, b) = await SeedDocumentsAsync();
        var (sql, parameters) = InsertDocumentRow(table, a);

        await using (var session = await Session.BeginAsync(AppCore))
        {
            Assert.Empty(
                await session.ColumnAsync<Guid>(
                    $"SELECT tenant_id FROM core.{table} WHERE tenant_id = ANY($1)",
                    new[] { a.Tenant, b.Tenant }
                )
            );
            Assert.Equal(
                0,
                await session.ExecuteAsync($"UPDATE core.{table} SET tenant_id = tenant_id")
            );
        }
        await using (var session = await Session.BeginAsync(AppCore))
        {
            await session.AssertDeniedAsync(sql, parameters);
        }
    }

    [Fact]
    public async Task A_document_cannot_point_at_another_tenants_establishment_user_or_document()
    {
        // Row-level security accepts these rows (their tenant is the session's), but the composite
        // foreign keys find no establishment, document or user with that id in this tenant.
        var (a, b) = await SeedDocumentsAsync();

        foreach (
            var (sql, parameters) in new (string, object[])[]
            {
                (InsertDocument, [Guid.CreateVersion7(), a.Tenant, b.Establishment]),
                (InsertVersion, [Guid.CreateVersion7(), a.Tenant, b.Document, 2, a.User]),
                (InsertVersion, [Guid.CreateVersion7(), a.Tenant, a.Document, 2, b.User]),
            }
        )
        {
            await using var session = await Session.BeginAsync(AppCore, a.Tenant);
            var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
                session.ExecuteAsync(sql, parameters)
            );
            Assert.Equal("23503", error.SqlState); // foreign_key_violation
        }
    }

    /// <summary>
    /// The inspection rows of one tenant: a user, a completed inspection of its establishment, a
    /// finding with an action item and a spare finding without one, and a day's count.
    /// </summary>
    private sealed record Inspections(
        Guid Tenant,
        Guid Establishment,
        Guid User,
        Guid Inspection,
        Guid Finding,
        Guid SpareFinding,
        Guid ActionItem
    );

    private static readonly DateOnly CountedDay = new(2031, 1, 1);

    private const string InsertInspection = """
        INSERT INTO core.inspections (id, tenant_id, establishment_id, status, requested_by,
            requested_at, items_total, items_evaluated, themes)
        VALUES ($1, $2, $3, $4, 'user', now(), 0, 0, '[]')
        """;
    private const string InsertFinding = """
        INSERT INTO core.findings (id, tenant_id, inspection_id, item_id, theme, title, severity,
            verdict, confidence, rationale, review_status, regulation_citations, evidence_citations)
        VALUES ($1, $2, $3, 'us-temp-01', 'temperature_control', 'Cold holding', 'critical',
            'non_compliant', 'high', 'The log shows 45 F.', 'Pending', '[]', '[]')
        """;
    private const string InsertActionItem = """
        INSERT INTO core.action_items (id, tenant_id, finding_id, text, due_on, status)
        VALUES ($1, $2, $3, 'Recalibrate the cooler.', DATE '2031-01-08', 'Open')
        """;
    private const string InsertInspectionCount =
        "INSERT INTO core.inspection_counts (tenant_id, day, count) VALUES ($1, $2, 1)";

    /// <summary>Two new tenants with their inspection rows, written by app_core.</summary>
    private async Task<(Inspections A, Inspections B)> SeedInspectionsAsync()
    {
        var (tenantA, tenantB, establishmentA, establishmentB) = await SeedAsync();
        var seeded = new List<Inspections>();
        foreach (
            var (tenant, establishment) in new[]
            {
                (tenantA, establishmentA),
                (tenantB, establishmentB),
            }
        )
        {
            var rows = new Inspections(
                tenant,
                establishment,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                Guid.CreateVersion7()
            );
            await using var session = await Session.BeginAsync(AppCore, tenant);
            await session.ExecuteAsync(
                "INSERT INTO core.users (tenant_id, id, sub) VALUES ($1, $2, 'user')",
                tenant,
                rows.User
            );
            await session.ExecuteAsync(
                InsertInspection,
                rows.Inspection,
                tenant,
                establishment,
                "Completed"
            );
            await session.ExecuteAsync(InsertFinding, rows.Finding, tenant, rows.Inspection);
            await session.ExecuteAsync(InsertFinding, rows.SpareFinding, tenant, rows.Inspection);
            await session.ExecuteAsync(InsertActionItem, rows.ActionItem, tenant, rows.Finding);
            await session.ExecuteAsync(InsertInspectionCount, tenant, CountedDay);
            await session.CommitAsync();
            seeded.Add(rows);
        }
        return (seeded[0], seeded[1]);
    }

    /// <summary>A new row for <paramref name="table"/> carrying <paramref name="target"/>'s tenant and ids.</summary>
    private static (string Sql, object[] Parameters) InsertInspectionRow(
        string table,
        Inspections target
    ) =>
        table switch
        {
            "inspections" => (
                InsertInspection,
                [Guid.CreateVersion7(), target.Tenant, target.Establishment, "Completed"]
            ),
            "findings" => (
                InsertFinding,
                [Guid.CreateVersion7(), target.Tenant, target.Inspection]
            ),
            "action_items" => (
                InsertActionItem,
                [Guid.CreateVersion7(), target.Tenant, target.SpareFinding]
            ),
            _ => (InsertInspectionCount, [target.Tenant, CountedDay.AddDays(1)]),
        };

    [Theory]
    [InlineData("inspections")]
    [InlineData("findings")]
    [InlineData("action_items")]
    [InlineData("inspection_counts")]
    public async Task Each_tenant_reads_only_its_own_inspection_rows(string table)
    {
        var (a, b) = await SeedInspectionsAsync();
        var visible = $"SELECT DISTINCT tenant_id FROM core.{table} WHERE tenant_id = ANY($1)";
        Guid[] both = [a.Tenant, b.Tenant];

        await using (var session = await Session.BeginAsync(AppCore, a.Tenant))
        {
            Assert.Equal([a.Tenant], await session.ColumnAsync<Guid>(visible, both));
        }
        await using (var session = await Session.BeginAsync(AppCore, b.Tenant))
        {
            Assert.Equal([b.Tenant], await session.ColumnAsync<Guid>(visible, both));
        }
    }

    [Theory]
    [InlineData("UPDATE core.inspections SET status = 'Failed' WHERE tenant_id = $1")]
    [InlineData("UPDATE core.findings SET review_status = 'Rejected' WHERE tenant_id = $1")]
    [InlineData("UPDATE core.action_items SET status = 'Done' WHERE tenant_id = $1")]
    [InlineData("UPDATE core.inspection_counts SET count = 99 WHERE tenant_id = $1")]
    public async Task A_tenant_cannot_update_another_tenants_inspection_rows(string sql)
    {
        var (a, b) = await SeedInspectionsAsync();

        await using (var session = await Session.BeginAsync(AppCore, b.Tenant))
        {
            Assert.Equal(0, await session.ExecuteAsync(sql, a.Tenant));
            await session.CommitAsync();
        }

        await using var sessionA = await Session.BeginAsync(AppCore, a.Tenant);
        Assert.Equal(
            ("Completed", 2L, "Open", 1),
            (
                await sessionA.ScalarAsync<string>("SELECT status FROM core.inspections"),
                await sessionA.ScalarAsync<long>(
                    "SELECT count(*) FROM core.findings WHERE review_status = 'Pending'"
                ),
                await sessionA.ScalarAsync<string>("SELECT status FROM core.action_items"),
                await sessionA.ScalarAsync<int>("SELECT count FROM core.inspection_counts")
            )
        );
    }

    [Theory]
    [InlineData("inspections")]
    [InlineData("findings")]
    [InlineData("action_items")]
    [InlineData("inspection_counts")]
    public async Task App_core_cannot_delete_inspection_rows_even_its_own(string table)
    {
        var (a, _) = await SeedInspectionsAsync();

        await using var session = await Session.BeginAsync(AppCore, a.Tenant);
        await session.AssertDeniedAsync($"DELETE FROM core.{table} WHERE tenant_id = $1", a.Tenant);
    }

    [Theory]
    [InlineData("inspections")]
    [InlineData("findings")]
    [InlineData("action_items")]
    [InlineData("inspection_counts")]
    public async Task A_tenant_cannot_insert_or_move_inspection_rows_into_another_tenant(
        string table
    )
    {
        var (a, b) = await SeedInspectionsAsync();
        var (sql, parameters) = InsertInspectionRow(table, b);

        await using (var session = await Session.BeginAsync(AppCore, a.Tenant))
        {
            await session.AssertDeniedAsync(sql, parameters);
        }
        await using (var session = await Session.BeginAsync(AppCore, a.Tenant))
        {
            await session.AssertDeniedAsync(
                $"UPDATE core.{table} SET tenant_id = $1 WHERE tenant_id = $2",
                b.Tenant,
                a.Tenant
            );
        }
    }

    [Theory]
    [InlineData("inspections")]
    [InlineData("findings")]
    [InlineData("action_items")]
    [InlineData("inspection_counts")]
    public async Task Without_a_tenant_no_inspection_row_is_visible_and_writes_fail(string table)
    {
        var (a, b) = await SeedInspectionsAsync();
        var (sql, parameters) = InsertInspectionRow(table, a);

        await using (var session = await Session.BeginAsync(AppCore))
        {
            Assert.Empty(
                await session.ColumnAsync<Guid>(
                    $"SELECT tenant_id FROM core.{table} WHERE tenant_id = ANY($1)",
                    new[] { a.Tenant, b.Tenant }
                )
            );
            Assert.Equal(
                0,
                await session.ExecuteAsync($"UPDATE core.{table} SET tenant_id = tenant_id")
            );
        }
        await using (var session = await Session.BeginAsync(AppCore))
        {
            await session.AssertDeniedAsync(sql, parameters);
        }
    }

    [Fact]
    public async Task An_inspection_row_cannot_point_at_another_tenants_rows()
    {
        // Row-level security accepts these rows (their tenant is the session's), but the composite
        // foreign keys find no establishment, inspection, finding or user with that id here.
        var (a, b) = await SeedInspectionsAsync();

        foreach (
            var (sql, parameters) in new (string, object[])[]
            {
                (InsertInspection, [Guid.CreateVersion7(), a.Tenant, b.Establishment, "Completed"]),
                (InsertFinding, [Guid.CreateVersion7(), a.Tenant, b.Inspection]),
                (InsertActionItem, [Guid.CreateVersion7(), a.Tenant, b.SpareFinding]),
                ("UPDATE core.findings SET reviewed_by = $2 WHERE id = $1", [a.Finding, b.User]),
                (
                    "UPDATE core.action_items SET updated_by = $2 WHERE id = $1",
                    [a.ActionItem, b.User]
                ),
            }
        )
        {
            await using var session = await Session.BeginAsync(AppCore, a.Tenant);
            var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
                session.ExecuteAsync(sql, parameters)
            );
            Assert.Equal("23503", error.SqlState); // foreign_key_violation
        }
    }

    [Theory]
    [InlineData("Requested", "Requested")]
    [InlineData("Requested", "Running")]
    [InlineData("Running", "Requested")]
    public async Task An_establishment_has_one_inspection_in_flight_at_most(
        string first,
        string second
    )
    {
        var (a, _) = await SeedInspectionsAsync();
        await using var session = await Session.BeginAsync(AppCore, a.Tenant);
        await session.ExecuteAsync(
            InsertInspection,
            Guid.CreateVersion7(),
            a.Tenant,
            a.Establishment,
            first
        );

        var error = await Assert.ThrowsAsync<Npgsql.PostgresException>(() =>
            session.ExecuteAsync(
                InsertInspection,
                Guid.CreateVersion7(),
                a.Tenant,
                a.Establishment,
                second
            )
        );
        // unique_violation, on the index a concurrent start maps to its refusal
        Assert.Equal(
            ("23505", "ix_inspections_tenant_id_establishment_id"),
            (error.SqlState, error.ConstraintName)
        );
    }

    [Fact]
    public async Task Ended_inspections_and_other_establishments_do_not_count_as_in_flight()
    {
        // The seeded inspection is Completed already.
        var (a, _) = await SeedInspectionsAsync();
        var other = Guid.CreateVersion7();
        await using var session = await Session.BeginAsync(AppCore, a.Tenant);
        await session.ExecuteAsync(InsertEstablishment, other, a.Tenant);

        foreach (
            var (establishment, status) in new[]
            {
                (a.Establishment, "Failed"),
                (a.Establishment, "Running"),
                (other, "Requested"),
            }
        )
        {
            await session.ExecuteAsync(
                InsertInspection,
                Guid.CreateVersion7(),
                a.Tenant,
                establishment,
                status
            );
        }
        // Once the run ends, the next one may start.
        await session.ExecuteAsync(
            "UPDATE core.inspections SET status = 'Completed' WHERE status = 'Running'"
        );
        await session.ExecuteAsync(
            InsertInspection,
            Guid.CreateVersion7(),
            a.Tenant,
            a.Establishment,
            "Requested"
        );

        Assert.Equal(5L, await session.ScalarAsync<long>("SELECT count(*) FROM core.inspections"));
    }
}
