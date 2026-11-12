using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Normetry.Infrastructure.Persistence;

namespace Normetry.IntegrationTests;

/// <summary>
/// The API in memory. It trusts an issuer and a signing key generated for this test run instead
/// of Keycloak's metadata, so no identity provider is needed; the rest of the token pipeline
/// (audience, lifetime, claims, policies) is the real one.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://issuer.test/realms/normetry";
    public const string UserId = "0b5a4c8e-7f1d-4e2a-9c3b-6d8e1f2a3b4c";

    // The demo tenants: the API's Development seed creates them when a database is attached.
    public const string TenantA = "11111111-1111-4111-8111-111111111111";
    public const string TenantB = "22222222-2222-4222-8222-222222222222";

    private static readonly SecurityKey SigningKey = NewSigningKey();

    /// <summary>
    /// The <c>app_core</c> connection string of a test database (see <c>DatabaseFixture</c>).
    /// Without one the API gets an address it never connects to, no role check and no demo seed,
    /// which is enough for endpoints that do not touch the database.
    /// </summary>
    public string? CoreConnectionString { get; init; }

    public static SecurityKey NewSigningKey() =>
        new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-key" };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:core", CoreConnectionString ?? "Host=db.invalid");
        builder.ConfigureTestServices(services =>
        {
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                // A static configuration replaces the download from the authority.
                options.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                options.Configuration.SigningKeys.Add(SigningKey);
            });
            if (CoreConnectionString is null)
            {
                foreach (
                    var seed in services
                        .Where(service =>
                            service.ImplementationType == typeof(DemoTenantSeed)
                            || service.ImplementationType == typeof(RuntimeRoleCheck)
                        )
                        .ToArray()
                )
                {
                    services.Remove(seed);
                }
            }
        });
    }

    /// <summary>A token for <paramref name="tenantId"/> holding only <paramref name="role"/>.</summary>
    public static string CreateToken(string tenantId, string role) =>
        CreateToken(token =>
        {
            token.Claims["tenant_id"] = tenantId;
            token.Claims["realm_access"] = new Dictionary<string, object>
            {
                ["roles"] = new[] { role },
            };
        });

    /// <summary>
    /// An access token shaped like Keycloak's for a Demo Bistro Owner, signed with the trusted
    /// key. <paramref name="customize"/> changes it for a single test.
    /// </summary>
    public static string CreateToken(Action<SecurityTokenDescriptor>? customize = null)
    {
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = "normetry-api",
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = UserId,
                ["azp"] = "normetry-web",
                ["tenant_id"] = TenantA,
                ["realm_access"] = new Dictionary<string, object>
                {
                    ["roles"] = new[] { "Owner", "offline_access", "default-roles-normetry" },
                },
            },
        };
        customize?.Invoke(descriptor);
        // Off so that a test can drop exp: the times above are always set explicitly.
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(
            descriptor
        );
    }
}

public static class ApiFactoryExtensions
{
    public static HttpClient CreateClient(
        this WebApplicationFactory<Program> factory,
        string? accessToken
    )
    {
        var client = factory.CreateClient();
        if (accessToken is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                accessToken
            );
        }
        return client;
    }
}
