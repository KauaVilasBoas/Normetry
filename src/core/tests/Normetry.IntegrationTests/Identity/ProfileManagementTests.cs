using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Normetry.Application.Identity;
using Normetry.Domain.Identity;
using Normetry.IntegrationTests.Database;
using Xunit;

namespace Normetry.IntegrationTests.Identity;

/// <summary>
/// The profile and user management API, end to end against the test database as app_core. Every
/// test makes its own tenant, whose caller is its only Owner.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ProfileManagementTests
{
    private readonly DatabaseFixture db;

    public ProfileManagementTests(DatabaseFixture db)
    {
        db.SkipWithoutDocker();
        this.db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Client(Guid tenant, string sub) =>
        db.Api.CreateClient(ApiFactory.CreateToken(tenant.ToString(), sub));

    private async Task<(Guid Tenant, HttpClient Owner)> NewTenantAsync()
    {
        var owner = Guid.NewGuid().ToString();
        var tenant = await TestTenants.CreateAsync(db, null, owner);
        return (tenant, Client(tenant, owner));
    }

    private static async Task<ProfileListItem[]> ProfilesAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<ProfileListItem[]>("/profiles", Ct))!;

    private static async Task<Guid> SystemProfileAsync(HttpClient client, string key) =>
        (await ProfilesAsync(client)).Single(profile => profile.SystemKey == key).Id;

    private static async Task<UserResponse[]> UsersAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<UserResponse[]>("/users", Ct))!;

    private static async Task<Guid> MeAsync(HttpClient client) =>
        (await UsersAsync(client)).Single(user => user.IsCurrentUser).Id;

    private static async Task<Guid> CreateAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/profiles", new { name }, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Created>(Ct))!.Id;
    }

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code
    )
    {
        Assert.Equal(status, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        Assert.Equal(code, problem!.Extensions["code"]!.ToString());
    }

    private static Task<HttpResponseMessage> PatchAsync(HttpClient client, Guid id, string name) =>
        client.PatchAsJsonAsync($"/profiles/{id}", new { name }, Ct);

    private static Task<HttpResponseMessage> SetPermissionsAsync(
        HttpClient client,
        Guid id,
        params string[] permissions
    ) => client.PutAsJsonAsync($"/profiles/{id}/permissions", new { permissions }, Ct);

    private sealed record Created(Guid Id);

    [Theory]
    [InlineData("create")]
    [InlineData("rename")]
    public async Task A_name_taken_by_a_concurrent_request_is_a_409_not_a_500(string change)
    {
        var (tenant, owner) = await NewTenantAsync();
        var renamed = change == "rename" ? await CreateAsync(owner, "Kitchen") : Guid.Empty;

        // Another request has inserted the name but not committed yet: the API's own check cannot
        // see it, and its insert waits on the unique index until that request commits.
        await using var concurrent = await Session.BeginAsync(
            db.ConnectionString("app_core"),
            tenant
        );
        await concurrent.ExecuteAsync(
            "INSERT INTO core.profiles (tenant_id, id, name, grants_all) VALUES ($1, $2, 'Bar', false)",
            tenant,
            Guid.CreateVersion7()
        );
        var request =
            change == "create"
                ? owner.PostAsJsonAsync("/profiles", new { name = "Bar" }, Ct)
                : PatchAsync(owner, renamed, "Bar");
        await Task.Delay(500, Ct);
        await concurrent.CommitAsync();

        await AssertProblemAsync(await request, HttpStatusCode.Conflict, "profile.name_taken");
    }

    [Fact]
    public async Task The_catalog_and_the_system_profiles_are_listed()
    {
        var (_, owner) = await NewTenantAsync();

        var catalog = await owner.GetFromJsonAsync<PermissionResponse[]>("/permissions", Ct);
        var profiles = await ProfilesAsync(owner);
        var ownerProfile = await owner.GetFromJsonAsync<ProfileResponse>(
            $"/profiles/{profiles.Single(profile => profile.GrantsAll).Id}",
            Ct
        );

        Assert.Equal(
            Permissions.All.Order(StringComparer.Ordinal),
            catalog!.Select(permission => permission.Name)
        );
        Assert.Equal(
            [
                ("Auditor", "auditor", false, 0),
                ("Manager", "manager", false, 0),
                ("Owner", "owner", true, Permissions.All.Count),
            ],
            profiles.Select(profile =>
                (profile.Name, profile.SystemKey, profile.GrantsAll, profile.PermissionCount)
            )
        );
        Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), ownerProfile!.Permissions);
    }

    [Fact]
    public async Task A_new_profile_gets_201_and_a_duplicate_name_gets_409()
    {
        var (_, owner) = await NewTenantAsync();

        var response = await owner.PostAsJsonAsync("/profiles", new { name = "  Kitchen  " }, Ct);
        var created = await owner.GetFromJsonAsync<ProfileResponse>(response.Headers.Location, Ct);
        var duplicate = await owner.PostAsJsonAsync("/profiles", new { name = "Kitchen" }, Ct);
        var system = await owner.PostAsJsonAsync("/profiles", new { name = "Owner" }, Ct);
        var blank = await owner.PostAsJsonAsync("/profiles", new { name = " " }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/profiles/{created!.Id}", response.Headers.Location!.OriginalString);
        Assert.Equal(
            ("Kitchen", null, false),
            (created.Name, created.SystemKey, created.GrantsAll)
        );
        Assert.Empty(created.Permissions);
        await AssertProblemAsync(duplicate, HttpStatusCode.Conflict, "profile.name_taken");
        await AssertProblemAsync(system, HttpStatusCode.Conflict, "profile.name_taken");
        await AssertProblemAsync(blank, HttpStatusCode.BadRequest, "validation");
    }

    [Fact]
    public async Task Only_the_tenants_own_profiles_can_be_renamed()
    {
        var (_, owner) = await NewTenantAsync();
        var kitchen = await CreateAsync(owner, "Kitchen");
        await CreateAsync(owner, "Bar");

        var renamed = await PatchAsync(owner, kitchen, "Line cooks");
        var taken = await PatchAsync(owner, kitchen, "Bar");
        var manager = await PatchAsync(
            owner,
            await SystemProfileAsync(owner, Profile.SystemKeys.Manager),
            "Boss"
        );
        var ownerProfile = await PatchAsync(
            owner,
            await SystemProfileAsync(owner, Profile.SystemKeys.Owner),
            "Boss"
        );
        var unknown = await PatchAsync(owner, Guid.CreateVersion7(), "Boss");

        Assert.Equal(HttpStatusCode.NoContent, renamed.StatusCode);
        Assert.Contains(await ProfilesAsync(owner), profile => profile.Name == "Line cooks");
        await AssertProblemAsync(taken, HttpStatusCode.Conflict, "profile.name_taken");
        await AssertProblemAsync(manager, HttpStatusCode.Conflict, "profile.system_profile");
        await AssertProblemAsync(ownerProfile, HttpStatusCode.Conflict, "profile.system_profile");
        await AssertProblemAsync(unknown, HttpStatusCode.NotFound, "profile.not_found");
    }

    [Fact]
    public async Task Setting_permissions_adds_each_areas_read_and_Owner_cannot_be_changed()
    {
        var (_, owner) = await NewTenantAsync();
        var kitchen = await CreateAsync(owner, "Kitchen");

        var set = await SetPermissionsAsync(
            owner,
            kitchen,
            Permissions.Documents.Upload,
            Permissions.Chat.Ask
        );
        var stored = await owner.GetFromJsonAsync<ProfileResponse>($"/profiles/{kitchen}", Ct);
        var manager = await SetPermissionsAsync(
            owner,
            await SystemProfileAsync(owner, Profile.SystemKeys.Manager),
            Permissions.Establishments.Manage
        );
        var unknown = await SetPermissionsAsync(owner, kitchen, "reports.export");
        var ownerProfile = await SetPermissionsAsync(
            owner,
            await SystemProfileAsync(owner, Profile.SystemKeys.Owner),
            Permissions.Chat.Ask
        );

        string[] expected =
        [
            Permissions.Chat.Ask,
            Permissions.Documents.Read,
            Permissions.Documents.Upload,
        ];
        Assert.Equal(HttpStatusCode.OK, set.StatusCode);
        Assert.Equal(
            expected,
            (await set.Content.ReadFromJsonAsync<ProfileResponse>(Ct))!.Permissions
        );
        Assert.Equal(expected, stored!.Permissions);
        Assert.Equal(
            [Permissions.Establishments.Manage, Permissions.Establishments.Read],
            (await manager.Content.ReadFromJsonAsync<ProfileResponse>(Ct))!.Permissions
        );
        await AssertProblemAsync(unknown, HttpStatusCode.BadRequest, "validation");
        await AssertProblemAsync(ownerProfile, HttpStatusCode.Conflict, "profile.owner_locked");
    }

    [Fact]
    public async Task Deleting_a_profile_takes_it_from_its_users_and_system_profiles_stay()
    {
        var (_, owner) = await NewTenantAsync();
        var kitchen = await CreateAsync(owner, "Kitchen");
        var me = await MeAsync(owner);
        (
            await owner.PutAsync($"/users/{me}/profiles/{kitchen}", null, Ct)
        ).EnsureSuccessStatusCode();

        var deleted = await owner.DeleteAsync($"/profiles/{kitchen}", Ct);
        var gone = await owner.GetAsync($"/profiles/{kitchen}", Ct);
        var system = await Task.WhenAll(
            Profile.SystemKeys.All.Select(async key =>
                await owner.DeleteAsync($"/profiles/{await SystemProfileAsync(owner, key)}", Ct)
            )
        );

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await AssertProblemAsync(gone, HttpStatusCode.NotFound, "profile.not_found");
        Assert.Equal(
            ["Owner"],
            (await UsersAsync(owner)).Single(user => user.Id == me).Profiles.Select(p => p.Name)
        );
        foreach (var response in system)
        {
            await AssertProblemAsync(response, HttpStatusCode.Conflict, "profile.system_profile");
        }
        Assert.Equal(3, (await ProfilesAsync(owner)).Length);
    }

    [Fact]
    public async Task The_user_list_marks_the_caller_and_shows_profiles_with_their_key()
    {
        var (tenant, owner) = await NewTenantAsync();
        var other = Guid.NewGuid().ToString();
        await TestTenants.GrantAsync(db, tenant, other, Permissions.Chat.Ask);

        var users = await UsersAsync(owner);

        Assert.Equal(2, users.Length);
        var me = users.Single(user => user.IsCurrentUser);
        Assert.Equal(
            ("Owner", Profile.SystemKeys.Owner),
            (Assert.Single(me.Profiles).Name, me.Profiles[0].SystemKey)
        );
        Assert.Null(Assert.Single(users.Single(user => !user.IsCurrentUser).Profiles).SystemKey);
    }

    [Fact]
    public async Task Assigning_and_unassigning_a_profile_changes_what_the_user_may_do()
    {
        var (tenant, owner) = await NewTenantAsync();
        var sub = Guid.NewGuid().ToString();
        using var user = Client(tenant, sub);
        (await user.GetAsync("/me", Ct)).EnsureSuccessStatusCode(); // first sign-in
        var userId = (await UsersAsync(owner)).Single(candidate => !candidate.IsCurrentUser).Id;
        var readers = await CreateAsync(owner, "Readers");
        (
            await SetPermissionsAsync(owner, readers, Permissions.Establishments.Read)
        ).EnsureSuccessStatusCode();
        var path = $"/users/{userId}/profiles/{readers}";

        var before = await user.GetAsync("/establishments", Ct);
        var assigned = await owner.PutAsync(path, null, Ct);
        var again = await owner.PutAsync(path, null, Ct);
        var during = await user.GetAsync("/establishments", Ct);
        var unassigned = await owner.DeleteAsync(path, Ct);
        var after = await user.GetAsync("/establishments", Ct);
        var unknownUser = await owner.PutAsync(
            $"/users/{Guid.CreateVersion7()}/profiles/{readers}",
            null,
            Ct
        );
        var unknownProfile = await owner.PutAsync(
            $"/users/{userId}/profiles/{Guid.CreateVersion7()}",
            null,
            Ct
        );

        Assert.Equal(
            (
                HttpStatusCode.Forbidden,
                HttpStatusCode.NoContent,
                HttpStatusCode.NoContent,
                HttpStatusCode.OK,
                HttpStatusCode.NoContent,
                HttpStatusCode.Forbidden
            ),
            (
                before.StatusCode,
                assigned.StatusCode,
                again.StatusCode,
                during.StatusCode,
                unassigned.StatusCode,
                after.StatusCode
            )
        );
        await AssertProblemAsync(unknownUser, HttpStatusCode.NotFound, "user.not_found");
        await AssertProblemAsync(unknownProfile, HttpStatusCode.NotFound, "profile.not_found");
    }

    [Fact]
    public async Task The_last_Owner_cannot_lose_the_Owner_profile()
    {
        var (tenant, owner) = await NewTenantAsync();
        var ownerProfile = await SystemProfileAsync(owner, Profile.SystemKeys.Owner);
        var me = await MeAsync(owner);
        var otherSub = Guid.NewGuid().ToString();
        using var other = Client(tenant, otherSub);
        (await other.GetAsync("/me", Ct)).EnsureSuccessStatusCode();
        var otherId = (await UsersAsync(owner)).Single(user => !user.IsCurrentUser).Id;

        var alone = await owner.DeleteAsync($"/users/{me}/profiles/{ownerProfile}", Ct);
        (
            await owner.PutAsync($"/users/{otherId}/profiles/{ownerProfile}", null, Ct)
        ).EnsureSuccessStatusCode();
        var handedOver = await owner.DeleteAsync($"/users/{me}/profiles/{ownerProfile}", Ct);
        var lastAgain = await other.DeleteAsync($"/users/{otherId}/profiles/{ownerProfile}", Ct);

        await AssertProblemAsync(alone, HttpStatusCode.Conflict, "profile.last_owner");
        Assert.Equal(HttpStatusCode.NoContent, handedOver.StatusCode);
        await AssertProblemAsync(lastAgain, HttpStatusCode.Conflict, "profile.last_owner");
    }

    [Fact]
    public async Task Two_Owners_removing_each_other_at_once_leave_one_Owner()
    {
        var (a, b) = (Guid.NewGuid().ToString(), Guid.NewGuid().ToString());
        var tenant = await TestTenants.CreateAsync(db, null, a, b);
        using var clientA = Client(tenant, a);
        using var clientB = Client(tenant, b);
        var ownerProfile = await SystemProfileAsync(clientA, Profile.SystemKeys.Owner);
        var (idA, idB) = (await MeAsync(clientA), await MeAsync(clientB));

        var responses = await Task.WhenAll(
            clientA.DeleteAsync($"/users/{idB}/profiles/{ownerProfile}", Ct),
            clientB.DeleteAsync($"/users/{idA}/profiles/{ownerProfile}", Ct)
        );

        // The second request was authorized before the first committed; the lock still stops it.
        Assert.Equal(
            [HttpStatusCode.NoContent, HttpStatusCode.Conflict],
            responses.Select(response => response.StatusCode).Order()
        );
        await using var session = await Session.BeginAsync(db.ConnectionString("app_core"), tenant);
        Assert.Equal(
            1L,
            await session.ScalarAsync<long>(
                "SELECT count(*) FROM core.user_profiles WHERE profile_id = $1",
                ownerProfile
            )
        );
    }

    [Fact]
    public async Task Another_tenants_profiles_and_users_are_out_of_reach()
    {
        var (_, ownerA) = await NewTenantAsync();
        var (_, ownerB) = await NewTenantAsync();
        var secret = await CreateAsync(ownerB, "Secret");
        var userB = await MeAsync(ownerB);
        var ownerProfileB = await SystemProfileAsync(ownerB, Profile.SystemKeys.Owner);
        var meA = await MeAsync(ownerA);
        var ownerProfileA = await SystemProfileAsync(ownerA, Profile.SystemKeys.Owner);

        var attempts = new (Task<HttpResponseMessage> Call, string Code)[]
        {
            (ownerA.GetAsync($"/profiles/{secret}", Ct), "profile.not_found"),
            (PatchAsync(ownerA, secret, "Taken"), "profile.not_found"),
            (SetPermissionsAsync(ownerA, secret, Permissions.Chat.Ask), "profile.not_found"),
            (ownerA.DeleteAsync($"/profiles/{secret}", Ct), "profile.not_found"),
            (
                ownerA.PutAsync($"/users/{userB}/profiles/{ownerProfileA}", null, Ct),
                "user.not_found"
            ),
            (
                ownerA.PutAsync($"/users/{meA}/profiles/{ownerProfileB}", null, Ct),
                "profile.not_found"
            ),
            (ownerA.DeleteAsync($"/users/{userB}/profiles/{ownerProfileB}", Ct), "user.not_found"),
        };
        foreach (var (call, code) in attempts)
        {
            await AssertProblemAsync(await call, HttpStatusCode.NotFound, code);
        }

        Assert.DoesNotContain(await ProfilesAsync(ownerA), profile => profile.Id == secret);
        Assert.DoesNotContain(await UsersAsync(ownerA), user => user.Id == userB);
        var secretNow = await ownerB.GetFromJsonAsync<ProfileResponse>($"/profiles/{secret}", Ct);
        Assert.Equal(("Secret", 0), (secretNow!.Name, secretNow.Permissions.Count));
        Assert.Equal(
            ["Owner"],
            (await UsersAsync(ownerB)).Single(user => user.Id == userB).Profiles.Select(p => p.Name)
        );
    }
}
