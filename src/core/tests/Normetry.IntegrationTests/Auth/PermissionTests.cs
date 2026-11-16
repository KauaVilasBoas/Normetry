using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Normetry.Api.Auth;
using Normetry.Domain.Identity;
using Normetry.IntegrationTests.Database;
using Xunit;

namespace Normetry.IntegrationTests.Auth;

/// <summary>
/// Every action against its permission: a user with every other permission gets 403, a user with
/// only that one gets through. Each test makes its own tenant and users.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PermissionTests
{
    private readonly DatabaseFixture db;

    public PermissionTests(DatabaseFixture db)
    {
        db.SkipWithoutDocker();
        this.db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Method, path ({id} is a new UUID, so a request that gets through may be a 404) and permission.</summary>
    public static TheoryData<string, string, string> Actions =>
        new()
        {
            { "GET", "/establishments", Permissions.Establishments.Read },
            { "GET", "/establishments/{id}", Permissions.Establishments.Read },
            { "POST", "/establishments", Permissions.Establishments.Manage },
            { "POST", "/establishments/{id}/documents", Permissions.Documents.Upload },
            { "GET", "/establishments/{id}/documents", Permissions.Documents.Read },
            { "GET", "/documents/{id}", Permissions.Documents.Read },
            { "POST", "/documents/{id}/versions", Permissions.Documents.Upload },
            { "DELETE", "/documents/{id}", Permissions.Documents.Delete },
            { "GET", "/chat/quota", Permissions.Chat.Ask },
            { "GET", "/ai/conversations", Permissions.Chat.Ask },
            { "POST", "/ai/establishments/{id}/conversations/c1/messages", Permissions.Chat.Ask },
            { "GET", "/permissions", Permissions.Profiles.Read },
            { "GET", "/profiles", Permissions.Profiles.Read },
            { "GET", "/profiles/{id}", Permissions.Profiles.Read },
            { "POST", "/profiles", Permissions.Profiles.Manage },
            { "PATCH", "/profiles/{id}", Permissions.Profiles.Manage },
            { "DELETE", "/profiles/{id}", Permissions.Profiles.Manage },
            { "PUT", "/profiles/{id}/permissions", Permissions.Profiles.Manage },
            { "GET", "/users", Permissions.Profiles.Read },
            { "PUT", "/users/{id}/profiles/{id}", Permissions.Profiles.Manage },
            { "DELETE", "/users/{id}/profiles/{id}", Permissions.Profiles.Manage },
        };

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task A_user_without_the_permission_gets_403_and_one_with_only_it_gets_through(
        string method,
        string path,
        string permission
    )
    {
        var tenant = await TestTenants.CreateAsync(db);
        var (without, with) = (Guid.NewGuid().ToString(), Guid.NewGuid().ToString());
        await TestTenants.GrantAsync(
            db,
            tenant,
            without,
            [.. Permissions.All.Where(name => name != permission)]
        );
        await TestTenants.GrantAsync(db, tenant, with, permission);
        path = path.Replace("{id}", Guid.CreateVersion7().ToString());

        using var denied = await SendAsync(tenant, without, method, path);
        using var allowed = await SendAsync(tenant, with, method, path);

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var problem = await denied.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        Assert.Equal("permission.denied", problem!.Extensions["code"]!.ToString());
        Assert.Contains(permission, problem.Detail);
        Assert.NotEqual(HttpStatusCode.Forbidden, allowed.StatusCode);
        Assert.NotEqual(HttpStatusCode.Unauthorized, allowed.StatusCode);
    }

    [Fact]
    public void Every_permission_an_action_requires_has_a_case()
    {
        var required = typeof(Program)
            .Assembly.GetTypes()
            .SelectMany(type => type.GetMethods().Append<MemberInfo>(type))
            .SelectMany(member => member.GetCustomAttributes<HasPermissionAttribute>())
            .Select(attribute => attribute.Permission)
            .Append(Permissions.Chat.Ask) // the YARP routes
            .ToHashSet();

        var covered = Actions.Select(row => (string)row.Data.Item3).ToHashSet();

        Assert.NotEmpty(required);
        Assert.Subset(covered, required);
    }

    private async Task<HttpResponseMessage> SendAsync(
        Guid tenant,
        string sub,
        string method,
        string path
    )
    {
        using var client = db.Api.CreateClient(ApiFactory.CreateToken(tenant.ToString(), sub));
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = method is "POST" or "PATCH" or "PUT"
                ? JsonContent.Create(
                    new
                    {
                        name = "Kitchen",
                        type = "Restaurant",
                        jurisdiction = "US",
                        permissions = Array.Empty<string>(),
                    }
                )
                : null,
        };
        return await client.SendAsync(request, Ct);
    }
}
