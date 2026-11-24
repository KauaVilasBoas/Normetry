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
using Normetry.Api.Inspections;
using Normetry.Application.Documents;
using Normetry.Infrastructure.Messaging;
using Normetry.Infrastructure.Persistence;
using Normetry.IntegrationTests.Documents;

namespace Normetry.IntegrationTests;

/// <summary>
/// The API in memory. It trusts an issuer and a signing key generated for this test run instead
/// of Keycloak's metadata, so no identity provider is needed; the rest of the token pipeline
/// (audience, lifetime, claims, policies) is the real one.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string Issuer = "https://issuer.test/realms/normetry";

    // The demo tenants: the API's Development seed creates them when a database is attached.
    public const string TenantA = "11111111-1111-4111-8111-111111111111";
    public const string TenantB = "22222222-2222-4222-8222-222222222222";

    // The demo users (their Keycloak ids, so the token's sub): the seed gives each a system profile.
    public const string BistroOwner = "11111111-0000-4000-8000-000000000001";

    /// <summary>The default token's <c>sub</c>: <c>bistro.owner</c>, the Demo Bistro Owner.</summary>
    public const string UserId = BistroOwner;
    public const string BistroManager = "11111111-0000-4000-8000-000000000002";
    public const string BakeryOwner = "22222222-0000-4000-8000-000000000001";
    public const string BakeryAuditor = "22222222-0000-4000-8000-000000000002";

    /// <summary>What the gateway sends to the AI service as <c>X-Normetry-Internal-Token</c>.</summary>
    public const string InternalToken = "test-internal-token-0123456789abcdef";

    private static readonly SecurityKey SigningKey = NewSigningKey();

    /// <summary>
    /// The <c>app_core</c> connection string of a test database (see <c>DatabaseFixture</c>).
    /// Without one the API gets an address it never connects to, no role check and no demo seed,
    /// which is enough for endpoints that do not touch the database.
    /// </summary>
    public string? CoreConnectionString { get; init; }

    /// <summary>
    /// The <c>core_dispatcher</c> connection string of the same test database, which the role
    /// check and the tenant list use. Set it whenever <see cref="CoreConnectionString"/> is set.
    /// </summary>
    public string? DispatcherConnectionString { get; init; }

    /// <summary>
    /// The AMQP URI of a test broker (see <c>RabbitMqFixture</c>). Without one the API gets an
    /// address it never connects to. Either way the background messaging services do not run:
    /// tests drive them directly, so no test publishes or consumes another test's messages.
    /// </summary>
    public string? RabbitMqConnectionString { get; init; }

    /// <summary>Stands in for Azure Blob Storage.</summary>
    public InMemoryBlobStore Blobs { get; } = new();

    /// <summary>Where the feedback export writes: a folder of this factory's own.</summary>
    public string FeedbackDirectory { get; } =
        Path.Combine(Path.GetTempPath(), $"normetry-feedback-{Guid.NewGuid():N}");

    public static SecurityKey NewSigningKey() =>
        new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-key" };

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:core", CoreConnectionString ?? "Host=db.invalid");
        builder.UseSetting(
            "ConnectionStrings:core-dispatcher",
            DispatcherConnectionString ?? "Host=db.invalid"
        );
        builder.UseSetting(
            "ConnectionStrings:rabbitmq",
            RabbitMqConnectionString ?? "amqp://guest:guest@127.0.0.1:9"
        );
        builder.UseSetting("ConnectionStrings:blobs", "UseDevelopmentStorage=true");
        // The development document seed runs, but finds nothing: tests call it with their own
        // folder and tenant, so the demo tenants never get the demo documents here.
        builder.UseSetting(
            DemoDocumentSeed.DirectorySetting,
            Path.Combine(Path.GetTempPath(), $"normetry-no-demo-documents-{Guid.NewGuid():N}")
        );
        // The feedback export never writes into the repository from a test.
        builder.UseSetting(FeedbackExportFile.DirectorySetting, FeedbackDirectory);
        builder.UseSetting("Ai:InternalToken", InternalToken);
        // Nothing listens there; the gateway tests point it at a stub AI service.
        builder.UseSetting("Services:ai:http:0", "http://127.0.0.1:9");
        // The whole run shares one API and the default token's user, so no test trips a limit;
        // RateLimitingTests lower them on an API of their own.
        foreach (
            var limit in new[]
            {
                "GlobalPerMinute",
                "UploadsPerMinute",
                "InspectionStartsPerMinute",
                "ChatPerMinute",
            }
        )
        {
            builder.UseSetting($"RateLimiting:{limit}", "100000");
        }
        builder.ConfigureTestServices(services =>
        {
            services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                // A static configuration replaces the download from the authority.
                options.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                options.Configuration.SigningKeys.Add(SigningKey);
            });
            services.AddSingleton<IDocumentBlobStore>(Blobs);
            foreach (
                var host in services
                    .Where(service =>
                        service.ImplementationType is { } type
                        && (
                            type == typeof(OutboxDispatcher)
                            || type == typeof(DocumentStatusConsumer)
                            || type == typeof(InspectionStatusConsumer)
                            || type == typeof(TimeoutSweeper)
                        )
                    )
                    .ToArray()
            )
            {
                services.Remove(host);
            }
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

    /// <summary>A token for the user <paramref name="sub"/> of <paramref name="tenantId"/>.</summary>
    public static string CreateToken(string tenantId, string sub) =>
        CreateToken(token =>
        {
            token.Claims["tenant_id"] = tenantId;
            token.Claims["sub"] = sub;
        });

    /// <summary>
    /// An access token shaped like Keycloak's for the Demo Bistro Owner (<c>bistro.owner</c>),
    /// signed with the trusted key. Its realm roles are Keycloak's defaults and authorize nothing.
    /// <paramref name="customize"/> changes it for a single test.
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
                    ["roles"] = new[] { "offline_access", "default-roles-normetry" },
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
