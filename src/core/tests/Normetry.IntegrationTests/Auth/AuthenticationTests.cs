using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Normetry.Application.Identity.GetCurrentUser;
using Normetry.Domain.Identity;
using Normetry.IntegrationTests.Database;
using Xunit;

namespace Normetry.IntegrationTests.Auth;

/// <summary>
/// Token validation and policies through <c>/me</c>, against the test database: <c>/me</c> reads
/// the caller's tenant row, so a 200 needs the seeded demo tenants.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AuthenticationTests
{
    private readonly DatabaseFixture db;
    private readonly ApiFactory factory;

    public AuthenticationTests(DatabaseFixture db)
    {
        db.SkipWithoutDocker();
        this.db = db;
        factory = db.Api;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_valid_token_gets_its_user_tenant_profiles_and_permissions()
    {
        using var client = factory.CreateClient(ApiFactory.CreateToken());

        var me = await client.GetFromJsonAsync<CurrentUserResponse>("/me", Ct);

        Assert.Equal(ApiFactory.UserId, me!.UserId);
        Assert.Equal(Guid.Parse(ApiFactory.TenantA), me.TenantId);
        Assert.Equal("Demo Bistro", me.TenantName);
        Assert.Equal(
            ("Owner", Profile.SystemKeys.Owner),
            (Assert.Single(me.Profiles).Name, me.Profiles[0].SystemKey)
        );
        Assert.Equal(Permissions.All.Order(StringComparer.Ordinal), me.Permissions);
    }

    [Fact]
    public async Task A_user_without_profiles_gets_403_with_a_code_everywhere_but_me()
    {
        // A realm role in the token changes nothing: permissions come from the core database.
        using var client = factory.CreateClient(
            ApiFactory.CreateToken(token =>
            {
                token.Claims["sub"] = Guid.NewGuid().ToString();
                token.Claims["realm_access"] = new Dictionary<string, object>
                {
                    ["roles"] = new[] { "Owner", "Manager", "Auditor" },
                };
            })
        );

        var me = await client.GetFromJsonAsync<CurrentUserResponse>("/me", Ct);

        Assert.Equal(
            ("Demo Bistro", 0, 0),
            (me!.TenantName, me.Permissions.Count, me.Profiles.Count)
        );
        foreach (var path in new[] { "/establishments", "/chat/quota", "/ai/conversations" })
        {
            var response = await client.GetAsync(path, Ct);
            var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal("permission.denied", problem!.Extensions["code"]!.ToString());
        }
    }

    [Fact]
    public async Task A_token_without_a_tenant_gets_a_403_without_the_permission_code()
    {
        using var client = factory.CreateClient(
            ApiFactory.CreateToken(token => token.Claims.Remove("tenant_id"))
        );

        var response = await client.GetAsync("/establishments", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.DoesNotContain("permission.denied", body);
    }

    [Fact]
    public async Task A_tenant_without_a_core_row_gets_no_name()
    {
        var tenant = Guid.NewGuid();
        using var client = factory.CreateClient(
            ApiFactory.CreateToken(token => token.Claims["tenant_id"] = tenant.ToString())
        );

        var me = await client.GetFromJsonAsync<CurrentUserResponse>("/me", Ct);

        Assert.Equal(tenant, me!.TenantId);
        Assert.Null(me.TenantName);
    }

    [Fact]
    public async Task A_request_without_a_token_gets_401()
    {
        using var client = factory.CreateClient(accessToken: null);

        var response = await client.GetAsync("/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static readonly Dictionary<string, Action<SecurityTokenDescriptor>> TokenDefects = new()
    {
        ["wrong audience"] = token => token.Audience = "account",
        ["expired"] = token =>
        {
            token.IssuedAt = token.NotBefore = DateTime.UtcNow.AddMinutes(-20);
            token.Expires = DateTime.UtcNow.AddMinutes(-10);
        },
        ["wrong issuer"] = token => token.Issuer = "https://other.test/realms/normetry",
        ["signed with another key"] = token =>
            token.SigningCredentials = new SigningCredentials(
                ApiFactory.NewSigningKey(),
                SecurityAlgorithms.RsaSha256
            ),
        ["unsigned (alg none)"] = token => token.SigningCredentials = null,
        ["no exp"] = token => token.Expires = null,
        ["nbf in the future"] = token =>
        {
            token.NotBefore = DateTime.UtcNow.AddMinutes(10);
            token.Expires = DateTime.UtcNow.AddMinutes(20);
        },
    };

    [Theory]
    [InlineData("wrong audience")]
    [InlineData("expired")]
    [InlineData("wrong issuer")]
    [InlineData("signed with another key")]
    [InlineData("unsigned (alg none)")]
    [InlineData("no exp")]
    [InlineData("nbf in the future")]
    public async Task An_invalid_token_gets_401(string defect)
    {
        using var client = factory.CreateClient(ApiFactory.CreateToken(TokenDefects[defect]));

        var response = await client.GetAsync("/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static readonly Dictionary<string, Action<SecurityTokenDescriptor>> ForbiddenDefects =
        new()
        {
            ["no sub"] = token => token.Claims.Remove("sub"),
            ["no tenant_id"] = token => token.Claims.Remove("tenant_id"),
            ["not a UUID"] = token => token.Claims["tenant_id"] = "demo-bistro",
            ["UUID without hyphens"] = token =>
                token.Claims["tenant_id"] = ApiFactory.TenantA.Replace("-", ""),
            ["UUID in braces"] = token => token.Claims["tenant_id"] = $"{{{ApiFactory.TenantA}}}",
            ["UUID with spaces"] = token => token.Claims["tenant_id"] = $" {ApiFactory.TenantA} ",
            ["empty UUID"] = token => token.Claims["tenant_id"] = Guid.Empty.ToString(),
            ["two tenant ids"] = token =>
                token.Claims["tenant_id"] = new[] { ApiFactory.TenantA, ApiFactory.TenantB },
        };

    [Theory]
    [InlineData("no sub")]
    [InlineData("no tenant_id")]
    [InlineData("not a UUID")]
    [InlineData("UUID without hyphens")]
    [InlineData("UUID in braces")]
    [InlineData("UUID with spaces")]
    [InlineData("empty UUID")]
    [InlineData("two tenant ids")]
    public async Task A_token_without_a_sub_or_one_valid_tenant_gets_403(string defect)
    {
        using var client = factory.CreateClient(ApiFactory.CreateToken(ForbiddenDefects[defect]));

        var response = await client.GetAsync("/me", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_token_with_an_empty_sub_gets_403_and_creates_no_user()
    {
        var tenant = await TestTenants.CreateAsync(db);
        using var client = factory.CreateClient(ApiFactory.CreateToken(tenant.ToString(), ""));

        var response = await client.GetAsync("/me", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await using var session = await Session.BeginAsync(db.ConnectionString("app_core"), tenant);
        Assert.Equal(0L, await session.ScalarAsync<long>("SELECT count(*) FROM core.users"));
    }

    [Fact]
    public async Task A_tenant_sent_by_the_client_is_ignored()
    {
        const string Path = $"/me?tenant_id={ApiFactory.TenantB}";
        using var client = factory.CreateClient(ApiFactory.CreateToken());
        client.DefaultRequestHeaders.Add("X-Normetry-Tenant-Id", ApiFactory.TenantB);
        using var withoutTenant = factory.CreateClient(
            ApiFactory.CreateToken(token => token.Claims.Remove("tenant_id"))
        );
        withoutTenant.DefaultRequestHeaders.Add("X-Normetry-Tenant-Id", ApiFactory.TenantB);

        var me = await client.GetFromJsonAsync<CurrentUserResponse>(Path, Ct);
        var response = await withoutTenant.GetAsync(Path, Ct);

        Assert.Equal(Guid.Parse(ApiFactory.TenantA), me!.TenantId);
        Assert.Equal("Demo Bistro", me.TenantName);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("Development", "normetry-web", HttpStatusCode.OK)]
    [InlineData("Development", "normetry-dev", HttpStatusCode.OK)]
    [InlineData("Development", "admin-cli", HttpStatusCode.Unauthorized)]
    [InlineData("Development", null, HttpStatusCode.Unauthorized)]
    [InlineData("Production", "normetry-web", HttpStatusCode.OK)]
    [InlineData("Production", "normetry-dev", HttpStatusCode.Unauthorized)]
    [InlineData("Production", "admin-cli", HttpStatusCode.Unauthorized)]
    [InlineData("Production", null, HttpStatusCode.Unauthorized)]
    public async Task Only_accepted_clients_get_in_and_the_dev_client_only_in_Development(
        string environment,
        string? clientId,
        HttpStatusCode expected
    )
    {
        await using var host = factory.WithWebHostBuilder(builder =>
            builder.UseEnvironment(environment)
        );
        using var client = host.CreateClient(
            ApiFactory.CreateToken(token =>
            {
                if (clientId is null)
                {
                    token.Claims.Remove("azp");
                }
                else
                {
                    token.Claims["azp"] = clientId;
                }
            })
        );

        var response = await client.GetAsync("/me", Ct);

        Assert.Equal(expected, response.StatusCode);
    }
}
