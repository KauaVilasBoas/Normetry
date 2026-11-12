using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Normetry.Api.Auth;
using Normetry.Application.Identity.GetCurrentUser;
using Xunit;

namespace Normetry.IntegrationTests.Auth;

public sealed class AuthenticationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_valid_token_gets_its_user_tenant_and_Normetry_roles()
    {
        using var client = factory.CreateClient(ApiFactory.CreateToken());

        var me = await client.GetFromJsonAsync<CurrentUserResponse>("/me", Ct);

        Assert.Equal(ApiFactory.UserId, me!.UserId);
        Assert.Equal(Guid.Parse(ApiFactory.TenantA), me.TenantId);
        // offline_access and default-roles-normetry are in the token but are not Normetry roles.
        Assert.Equal([NormetryRoles.Owner], me.Roles);
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

    [Theory]
    [InlineData(NormetryRoles.Owner, Policies.Owner, true)]
    [InlineData(NormetryRoles.Manager, Policies.Manager, true)]
    [InlineData(NormetryRoles.Auditor, Policies.Auditor, true)]
    [InlineData(NormetryRoles.Owner, Policies.Manager, false)]
    [InlineData(NormetryRoles.Auditor, Policies.Owner, false)]
    [InlineData("offline_access", Policies.Auditor, false)]
    [InlineData(NormetryRoles.Owner, Policies.OwnerOrManager, true)]
    [InlineData(NormetryRoles.Manager, Policies.OwnerOrManager, true)]
    [InlineData(NormetryRoles.Auditor, Policies.OwnerOrManager, false)]
    public async Task A_role_policy_requires_its_realm_role(
        string realmRole,
        string policy,
        bool allowed
    )
    {
        var token = ApiFactory.CreateToken(token =>
            token.Claims["realm_access"] = new Dictionary<string, object>
            {
                ["roles"] = new[] { realmRole },
            }
        );
        using var scope = factory.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Authorization = $"Bearer {token}";

        var authentication = await context.AuthenticateAsync();
        var authorization = await scope
            .ServiceProvider.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(authentication.Principal!, policy);

        Assert.True(authentication.Succeeded);
        Assert.Equal(allowed, authorization.Succeeded);
    }
}
