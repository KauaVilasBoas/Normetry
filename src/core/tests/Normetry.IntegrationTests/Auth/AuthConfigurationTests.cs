using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Normetry.Api.Auth;
using Xunit;

namespace Normetry.IntegrationTests.Auth;

/// <summary>What settings can and cannot change, and which endpoints are protected.</summary>
public sealed class AuthConfigurationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("https://attacker.test/realms/normetry")]
    [InlineData(ApiFactory.Issuer)]
    public async Task Settings_cannot_add_a_trusted_issuer_or_signing_key(string issuer)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        await using var host = WithBearerSettings(
            "Production",
            ("ValidIssuer", issuer),
            ("SigningKeys:0:Issuer", issuer),
            ("SigningKeys:0:Value", Convert.ToBase64String(key))
        );
        using var client = host.CreateClient(
            ApiFactory.CreateToken(token =>
            {
                token.Issuer = issuer;
                token.SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256
                );
            })
        );

        var response = await client.GetAsync("/me", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Settings_cannot_change_the_metadata_address()
    {
        const string MetadataAddress = "https://attacker.test/.well-known/openid-configuration";
        await using var host = WithBearerSettings(
            "Production",
            ("MetadataAddress", MetadataAddress)
        );

        Assert.NotEqual(MetadataAddress, BearerOptions(host).MetadataAddress);
    }

    [Fact]
    public async Task Production_requires_https_metadata_even_when_a_setting_turns_it_off()
    {
        await using var host = WithBearerSettings("Production", ("RequireHttpsMetadata", "false"));

        Assert.True(BearerOptions(host).RequireHttpsMetadata);
    }

    [Fact]
    public async Task Development_refuses_to_start_with_a_non_loopback_authority()
    {
        await using var host = WithBearerSettings(
            "Development",
            ("Authority", "https://login.normetry.example/realms/normetry")
        );

        var error = Assert.Throws<InvalidOperationException>(() => host.Services);

        Assert.Contains("loopback", error.Message);
    }

    [Fact]
    public void Every_endpoint_requires_a_tenant_policy_or_is_an_allowed_anonymous_endpoint()
    {
        string[] anonymousRoutes = ["/health", "/alive"];
        string[] tenantPolicies =
        [
            Policies.Tenant,
            Policies.Owner,
            Policies.Manager,
            Policies.Auditor,
        ];
        var endpoints = factory
            .Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .ToArray();

        Assert.Contains(endpoints, endpoint => endpoint.RoutePattern.RawText == "/me");
        Assert.All(endpoints, endpoint =>
        {
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            {
                Assert.Contains(endpoint.RoutePattern.RawText, anonymousRoutes);
            }
            else
            {
                Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(), data =>
                    tenantPolicies.Contains(data.Policy)
                );
            }
        });
    }

    [Fact]
    public async Task A_request_without_declared_authorization_falls_back_to_the_Tenant_policy()
    {
        using var anonymous = factory.CreateClient(accessToken: null);
        using var withoutTenant = factory.CreateClient(
            ApiFactory.CreateToken(token => token.Claims.Remove("tenant_id"))
        );
        using var valid = factory.CreateClient(ApiFactory.CreateToken());

        var anonymousResponse = await anonymous.GetAsync("/not-mapped", Ct);
        var withoutTenantResponse = await withoutTenant.GetAsync("/not-mapped", Ct);
        var validResponse = await valid.GetAsync("/not-mapped", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, withoutTenantResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, validResponse.StatusCode);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task Health_endpoints_stay_anonymous(string path)
    {
        using var client = factory.CreateClient(accessToken: null);

        var response = await client.GetAsync(path, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private WebApplicationFactory<Program> WithBearerSettings(
        string environment,
        params (string Key, string Value)[] settings
    ) =>
        factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            foreach (var (key, value) in settings)
            {
                builder.UseSetting($"Authentication:Schemes:Bearer:{key}", value);
            }
        });

    private static JwtBearerOptions BearerOptions(WebApplicationFactory<Program> host) =>
        host
            .Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);
}
