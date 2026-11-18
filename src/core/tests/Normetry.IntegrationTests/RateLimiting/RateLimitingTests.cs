using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Normetry.IntegrationTests.Database;
using Normetry.IntegrationTests.Documents;
using Xunit;
using static Normetry.IntegrationTests.Inspections.InspectionEndpointTests;

namespace Normetry.IntegrationTests.RateLimiting;

/// <summary>
/// Rate limits per tenant and user, each test on an API of its own with one limit lowered (the
/// shared test API raises them all). Callers are Owners, so permissions are never what is tested.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RateLimitingTests
{
    private readonly DatabaseFixture db;

    public RateLimitingTests(DatabaseFixture db)
    {
        db.SkipWithoutDocker();
        this.db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_burst_gets_429_with_Retry_After_while_other_users_and_tenants_go_on()
    {
        await using var api = Api(("RateLimiting:GlobalPerMinute", "2"));
        var (tenant, other) = (await NewTenantAsync(), await NewTenantAsync());
        using var user = Client(api, tenant, "user-1");
        using var sameTenantOtherUser = Client(api, tenant, "user-2");
        using var sameSubOtherTenant = Client(api, other, "user-1");

        for (var sent = 0; sent < 2; sent++)
        {
            using var served = await user.GetAsync("/establishments", Ct);
            Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        }
        using var refused = await user.GetAsync("/establishments", Ct);
        using var otherUser = await sameTenantOtherUser.GetAsync("/establishments", Ct);
        using var otherTenant = await sameSubOtherTenant.GetAsync("/establishments", Ct);

        await AssertProblemAsync(refused, HttpStatusCode.TooManyRequests, "rate_limited");
        Assert.Equal("application/problem+json", refused.Content.Headers.ContentType!.MediaType);
        // Two a minute: the next token comes within 30 seconds.
        Assert.InRange(
            refused.Headers.RetryAfter!.Delta!.Value,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(30)
        );
        Assert.Equal(HttpStatusCode.OK, otherUser.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherTenant.StatusCode);
    }

    [Fact]
    public async Task Requests_without_a_user_of_a_tenant_are_left_to_authorization()
    {
        await using var api = Api(("RateLimiting:GlobalPerMinute", "1"));
        using var anonymous = api.CreateClient(accessToken: null);
        using var noTenant = api.CreateClient(
            ApiFactory.CreateToken(token => token.Claims.Remove("tenant_id"))
        );

        for (var sent = 0; sent < 3; sent++)
        {
            using var unauthenticated = await anonymous.GetAsync("/establishments", Ct);
            using var forbidden = await noTenant.GetAsync("/establishments", Ct);
            Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        }
    }

    [Fact]
    public async Task A_new_document_and_a_new_version_share_the_upload_limit()
    {
        await using var api = Api(("RateLimiting:UploadsPerMinute", "1"));
        var tenant = await NewTenantAsync();
        var establishment = await TestTenants.CreateEstablishmentAsync(db, tenant);
        using var client = Client(api, tenant);

        var (document, _) = await DocumentEndpointTests.UploadPdfAsync(client, establishment);
        using var version = await DocumentEndpointTests.UploadVersionAsync(client, document);
        using var read = await client.GetAsync($"/documents/{document}", Ct);

        // Past the limit it would be 409 document.version_in_flight.
        await AssertProblemAsync(version, HttpStatusCode.TooManyRequests, "rate_limited");
        Assert.NotNull(version.Headers.RetryAfter?.Delta);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task Inspection_starts_have_their_own_limit()
    {
        await using var api = Api(("RateLimiting:InspectionStartsPerMinute", "1"));
        var tenant = await NewTenantAsync();
        var establishment = await TestTenants.CreateEstablishmentAsync(db, tenant);
        using var client = Client(api, tenant);
        var path = $"/establishments/{establishment}/inspections";

        using var first = await client.PostAsync(path, null, Ct);
        using var second = await client.PostAsync(path, null, Ct);
        using var list = await client.GetAsync(path, Ct);

        // The handler refuses the first (nothing to inspect), after it spent the token.
        await AssertProblemAsync(first, HttpStatusCode.Conflict, "no_indexed_documents");
        await AssertProblemAsync(second, HttpStatusCode.TooManyRequests, "rate_limited");
        Assert.NotNull(second.Headers.RetryAfter?.Delta);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Fact]
    public async Task Chat_messages_through_the_gateway_have_their_own_limit_and_a_refused_one_is_not_counted()
    {
        var hits = 0;
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var upstream = builder.Build();
        upstream.Map("/{**path}", () =>
        {
            Interlocked.Increment(ref hits);
            return Results.Ok();
        });
        await upstream.StartAsync(Ct);
        var address = upstream
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        await using var api = Api(
            ("RateLimiting:ChatPerMinute", "1"),
            ("Services:ai:http:0", address)
        );
        var tenant = await NewTenantAsync();
        var establishment = await TestTenants.CreateEstablishmentAsync(db, tenant);
        using var client = Client(api, tenant);
        var messages = $"/ai/establishments/{establishment}/conversations/c1/messages";

        using var sent = await client.PostAsync(messages, null, Ct);
        using var refused = await client.PostAsync(messages, null, Ct);
        using var conversations = await client.GetAsync("/ai/conversations", Ct);
        var quota = await client.GetFromJsonAsync<JsonElement>("/chat/quota", Ct);

        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        await AssertProblemAsync(refused, HttpStatusCode.TooManyRequests, "rate_limited");
        Assert.NotNull(refused.Headers.RetryAfter?.Delta);
        // The catch-all route takes only the global limit.
        Assert.Equal(HttpStatusCode.OK, conversations.StatusCode);
        Assert.Equal(2, hits);
        // Refused before the daily quota counted it (demo plan: 20).
        Assert.Equal(19, quota.GetProperty("remaining").GetInt32());
    }

    private WebApplicationFactory<Program> Api(params (string Key, string Value)[] settings) =>
        db.Api.WithWebHostBuilder(host =>
        {
            foreach (var (key, value) in settings)
            {
                host.UseSetting(key, value);
            }
        });

    private Task<Guid> NewTenantAsync() => TestTenants.CreateAsync(db, null, "user-1", "user-2");

    private static HttpClient Client(
        WebApplicationFactory<Program> api,
        Guid tenant,
        string user = "user-1"
    ) => api.CreateClient(ApiFactory.CreateToken(tenant.ToString(), user));
}
