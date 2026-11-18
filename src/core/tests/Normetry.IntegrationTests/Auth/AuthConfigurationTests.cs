using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Normetry.Api.Auth;
using Normetry.Api.Identity;
using Normetry.Domain.Identity;
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
        // Each permission policy includes Tenant (PermissionPolicyProvider).
        static bool IncludesTenant(string? policy) =>
            policy == Policies.Tenant
            || policy?.StartsWith(HasPermissionAttribute.PolicyPrefix, StringComparison.Ordinal)
                == true;
        var endpoints = factory
            .Services.GetRequiredService<EndpointDataSource>()
            .Endpoints.OfType<RouteEndpoint>()
            .ToArray();

        // Controller routes come without the leading slash ("me").
        Assert.Contains(endpoints, endpoint =>
            endpoint.RoutePattern.RawText?.TrimStart('/') == "me"
        );
        Assert.All(endpoints, endpoint =>
        {
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            {
                Assert.Contains(endpoint.RoutePattern.RawText, anonymousRoutes);
            }
            else
            {
                Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(), data =>
                    IncludesTenant(data.Policy)
                );
            }
        });

        // Both YARP routes are chat.
        var gateway = endpoints.Where(endpoint =>
            endpoint.RoutePattern.RawText!.StartsWith("/ai/", StringComparison.Ordinal)
        );
        Assert.Equal(2, gateway.Count());
        Assert.All(gateway, endpoint =>
            Assert.Contains(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(), data =>
                data.Policy == HasPermissionAttribute.PolicyName(Permissions.Chat.Ask)
            )
        );
    }

    [Fact]
    public void Every_controller_action_requires_a_permission_of_the_catalog_or_is_anonymous()
    {
        // The one exception: /me answers any tenant user, so that a user with no profile learns
        // that they have none. It still requires the Tenant policy (the test above).
        string[] withoutPermission = [$"{nameof(MeController)}.{nameof(MeController.Get)}"];
        var actions = typeof(Program)
            .Assembly.GetTypes()
            .Where(type => type.IsAssignableTo(typeof(ControllerBase)) && !type.IsAbstract)
            .SelectMany(controller =>
                controller
                    .GetMethods(
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
                    )
                    .Where(method =>
                        !method.IsSpecialName && !method.IsDefined(typeof(NonActionAttribute))
                    )
                    .Select(method => (Name: $"{controller.Name}.{method.Name}", Method: method))
            )
            .ToArray();

        Assert.Contains(actions, action => action.Name == "EstablishmentsController.Create");
        Assert.All(actions, action =>
        {
            var controller = action.Method.DeclaringType!;
            var permissions = action
                .Method.GetCustomAttributes<HasPermissionAttribute>()
                .Concat(controller.GetCustomAttributes<HasPermissionAttribute>())
                .ToArray();
            var anonymous =
                action.Method.IsDefined(typeof(AllowAnonymousAttribute))
                || controller.IsDefined(typeof(AllowAnonymousAttribute));

            Assert.True(
                permissions.Length > 0 || anonymous || withoutPermission.Contains(action.Name),
                $"{action.Name} has neither [HasPermission] nor [AllowAnonymous]."
            );
            Assert.All(permissions.SelectMany(attribute => attribute.AnyOf), name =>
                Assert.Contains(name, Permissions.All)
            );
        });
    }

    [Fact]
    public void Every_hub_and_hub_method_requires_a_permission_of_the_catalog()
    {
        // SignalR authorizes the hub's attributes once, when a client connects, and only the
        // method's own attributes on each call: both need one. The methods are the ones SignalR
        // exposes: public instance methods not declared by Hub or object.
        var hubs = typeof(Program)
            .Assembly.GetTypes()
            .Where(type => type.IsAssignableTo(typeof(Hub)) && !type.IsAbstract)
            .ToArray();
        var methods = hubs.SelectMany(hub =>
                hub.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(method =>
                        !method.IsSpecialName
                        && !method.GetBaseDefinition().DeclaringType!.IsAssignableFrom(typeof(Hub))
                    )
                    .Select(method => (Name: $"{hub.Name}.{method.Name}", Method: method))
            )
            .ToArray();

        Assert.Contains(methods, method => method.Name == "DocumentsHub.JoinEstablishment");
        Assert.All(hubs, hub =>
        {
            var permissions = hub.GetCustomAttributes<HasPermissionAttribute>().ToArray();
            Assert.True(permissions.Length > 0, $"{hub.Name} has no [HasPermission].");
            Assert.All(permissions.SelectMany(attribute => attribute.AnyOf), name =>
                Assert.Contains(name, Permissions.All)
            );
        });
        Assert.All(methods, method =>
        {
            var permissions = method
                .Method.GetCustomAttributes<HasPermissionAttribute>(inherit: false)
                .ToArray();
            Assert.True(permissions.Length > 0, $"{method.Name} has no [HasPermission].");
            Assert.All(permissions.SelectMany(attribute => attribute.AnyOf), name =>
                Assert.Contains(name, Permissions.All)
            );
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
