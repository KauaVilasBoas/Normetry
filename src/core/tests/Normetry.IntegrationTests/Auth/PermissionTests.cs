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
            { "PATCH", "/establishments/{id}/documents/{id}", Permissions.Documents.Upload },
            { "GET", "/documents/{id}", Permissions.Documents.Read },
            { "POST", "/documents/{id}/versions", Permissions.Documents.Upload },
            { "DELETE", "/documents/{id}", Permissions.Documents.Delete },
            { "POST", "/establishments/{id}/inspections", Permissions.Inspections.Run },
            { "GET", "/establishments/{id}/inspections", Permissions.Inspections.Read },
            { "GET", "/establishments/{id}/inspections/{id}", Permissions.Inspections.Read },
            {
                "POST",
                "/establishments/{id}/inspections/{id}/findings/{id}/accept",
                Permissions.Inspections.Review
            },
            {
                "POST",
                "/establishments/{id}/inspections/{id}/findings/{id}/reject",
                Permissions.Inspections.Review
            },
            {
                "PATCH",
                "/establishments/{id}/inspections/{id}/findings/{id}",
                Permissions.Inspections.Review
            },
            {
                "POST",
                "/establishments/{id}/inspections/{id}/action-items/{id}/done",
                Permissions.Inspections.Review
            },
            {
                "POST",
                "/establishments/{id}/inspections/{id}/action-items/{id}/reopen",
                Permissions.Inspections.Review
            },
            {
                "POST",
                "/establishments/{id}/inspections/{id}/feedback-export",
                Permissions.Inspections.Review
            },
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

    /// <summary>The one action open to any of two permissions (one attribute naming both).</summary>
    [Fact]
    public async Task The_overview_needs_inspections_read_or_documents_read()
    {
        var tenant = await TestTenants.CreateAsync(db);
        string[] either = [Permissions.Inspections.Read, Permissions.Documents.Read];
        var neither = Guid.NewGuid().ToString();
        await TestTenants.GrantAsync(db, tenant, neither, [.. Permissions.All.Except(either)]);
        var path = $"/establishments/{Guid.CreateVersion7()}/overview";

        using var denied = await SendAsync(tenant, neither, "GET", path);

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var problem = await denied.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        Assert.Equal("permission.denied", problem!.Extensions["code"]!.ToString());
        Assert.Contains("inspections.read or documents.read", problem.Detail);
        foreach (var permission in either)
        {
            var sub = Guid.NewGuid().ToString();
            await TestTenants.GrantAsync(db, tenant, sub, permission);
            using var allowed = await SendAsync(tenant, sub, "GET", path);
            Assert.Equal(HttpStatusCode.NotFound, allowed.StatusCode);
        }
    }

    [Fact]
    public void Every_permission_an_action_requires_has_a_case()
    {
        var required = typeof(Program)
            .Assembly.GetTypes()
            .SelectMany(type => type.GetMethods().Append<MemberInfo>(type))
            .SelectMany(member => member.GetCustomAttributes<HasPermissionAttribute>())
            .SelectMany(attribute => attribute.AnyOf)
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
