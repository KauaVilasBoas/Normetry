using Xunit;

namespace Normetry.IntegrationTests.Database;

/// <summary>
/// RLS on core.tenants, core.establishments, core.chat_message_counts, the identity tables
/// (profiles, profile_permissions, users, user_profiles) and the messaging tables
/// (outbox_messages, inbox_messages, outbox_tenants), in plain SQL as app_core (the rls-table
/// skill).
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

    [Fact]
    public async Task Outbox_tenants_are_readable_by_every_session_but_written_only_for_its_own_tenant()
    {
        // The one cross-tenant read: the dispatcher lists the tenants it visits. The rows hold
        // nothing but the id; every message is read under its tenant's RLS.
        const string Insert = "INSERT INTO core.outbox_tenants (tenant_id) VALUES ($1)";
        var (tenantA, tenantB, _, _) = await SeedAsync();

        await using (var session = await Session.BeginAsync(AppCore, tenantA))
        {
            await session.ExecuteAsync(Insert, tenantA);
            await session.CommitAsync();
        }
        await using (var session = await Session.BeginAsync(AppCore, tenantA))
        {
            await session.AssertDeniedAsync(Insert, tenantB);
        }
        await using (var session = await Session.BeginAsync(AppCore))
        {
            await session.AssertDeniedAsync(Insert, tenantB);
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

        await using var anyone = await Session.BeginAsync(AppCore);
        Assert.Equal(
            [tenantA],
            await anyone.ColumnAsync<Guid>(
                "SELECT tenant_id FROM core.outbox_tenants WHERE tenant_id = ANY($1)",
                new[] { tenantA, tenantB }
            )
        );
    }
}
