using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Normetry.Api.Gateway;
using Normetry.IntegrationTests.Database;
using Xunit;

namespace Normetry.IntegrationTests.Gateway;

/// <summary>
/// Establishment routes and the daily chat quota, against the test database as app_core and a
/// stub AI service that echoes what it received. Every test makes its own tenants, establishments
/// and users, and the clock is fixed one hour before a UTC midnight.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ChatGatewayTests : IAsyncLifetime
{
    private const int DemoLimit = 20;

    private static readonly DateTimeOffset Start = new(2031, 3, 14, 23, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture db;
    private readonly TestClock clock = new() { Now = Start };
    private readonly Guid unlimitedTenant = Guid.NewGuid();
    private WebApplication? upstream;
    private WebApplicationFactory<Program>? api;
    private int hits;

    public ChatGatewayTests(DatabaseFixture db)
    {
        db.SkipWithoutDocker();
        this.db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> Api => api!;

    public async ValueTask InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        upstream = builder.Build();
        upstream.Map("/{**path}", (HttpRequest request, HttpResponse response) =>
        {
            Interlocked.Increment(ref hits);
            if (request.Path.Value!.Contains("/fail/"))
            {
                return Results.StatusCode(StatusCodes.Status500InternalServerError);
            }
            return Results.Ok(
                new Echo(
                    request.Path.Value!,
                    request.Headers.ToDictionary(
                        header => header.Key,
                        header => header.Value.ToString(),
                        StringComparer.OrdinalIgnoreCase
                    )
                )
            );
        });
        await upstream.StartAsync(Ct);
        var address = upstream
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();

        api = db.Api.WithWebHostBuilder(host =>
        {
            host.UseSetting("Services:ai:http:0", address);
            host.UseSetting($"Chat:TenantPlans:{unlimitedTenant}", "unlimited");
            host.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(clock));
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (api is not null)
        {
            await api.DisposeAsync();
        }
        if (upstream is not null)
        {
            await upstream.DisposeAsync();
        }
    }

    [Fact]
    public async Task An_establishment_route_forwards_the_establishment_and_jurisdiction_loaded_by_the_core()
    {
        var tenant = await NewTenantAsync();
        var establishment = await NewEstablishmentAsync(tenant, "EU");
        using var client = Client(tenant);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/ai/establishments/{establishment}/conversations"
        );
        request.Headers.Add(InternalHeaders.EstablishmentId, Guid.NewGuid().ToString());
        request.Headers.Add("x-normetry-jurisdiction", "US");
        request.Headers.Add("X_Normetry_Jurisdiction", "BR");

        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echo = await response.Content.ReadFromJsonAsync<Echo>(Ct);
        Assert.Equal($"/establishments/{establishment}/conversations", echo!.Path);
        var headers = new Dictionary<string, string>(
            echo.Headers,
            StringComparer.OrdinalIgnoreCase
        );
        // One value each: a client copy would show up as a second, comma-joined value.
        Assert.Equal(establishment.ToString(), headers[InternalHeaders.EstablishmentId]);
        Assert.Equal("EU", headers[InternalHeaders.Jurisdiction]);
        Assert.Equal(tenant.ToString(), headers[InternalHeaders.TenantId]);
        Assert.Equal(
            new[]
            {
                InternalHeaders.EstablishmentId,
                InternalHeaders.Jurisdiction,
                InternalHeaders.TenantId,
                InternalHeaders.Token,
                InternalHeaders.UserId,
            }
                .Select(name => name.ToUpperInvariant())
                .Order(),
            headers
                .Keys.Select(name => name.Replace('_', '-').ToUpperInvariant())
                .Where(name => name.StartsWith(InternalHeaders.Prefix.ToUpperInvariant()))
                .Order()
        );
        // Creating a conversation is not a message: nothing counted.
        Assert.False(response.Headers.Contains("RateLimit-Limit"));
        Assert.Equal(DemoLimit, (await QuotaAsync(client)).Remaining);
    }

    [Theory]
    [InlineData("/conversations")]
    [InlineData("/conversations/c1/messages")]
    public async Task Another_tenants_establishment_gets_404_and_never_reaches_the_AI_service(
        string route
    )
    {
        var (owner, caller) = (await NewTenantAsync(), await NewTenantAsync());
        var establishment = await NewEstablishmentAsync(owner);
        using var client = Client(caller);

        using var response = await client.PostAsync(
            $"/ai/establishments/{establishment}{route}",
            null,
            Ct
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, hits);
        Assert.Equal(DemoLimit, (await QuotaAsync(client)).Remaining);
    }

    [Theory]
    [InlineData("/ai/establishments")]
    [InlineData("/ai/establishments/")]
    [InlineData("/ai/establishments/not-a-uuid/conversations")]
    [InlineData("/ai/Establishments/not-a-uuid/conversations/c1/messages")]
    [InlineData("/ai/establishments/{ID}/conversations")]
    [InlineData("/ai/establishments/{id}x/conversations")]
    [InlineData("/ai/establishments/{unknown}/conversations/c1/messages")]
    public async Task A_malformed_or_unknown_establishment_id_gets_404_and_never_reaches_the_AI_service(
        string path
    )
    {
        var tenant = await NewTenantAsync();
        var establishment = await NewEstablishmentAsync(tenant);
        using var client = Client(tenant);
        path = path.Replace("{id}", establishment.ToString())
            .Replace("{ID}", establishment.ToString().ToUpperInvariant())
            .Replace("{unknown}", Guid.CreateVersion7().ToString());

        using var response = await client.PostAsync(path, null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, hits);
        Assert.Equal(DemoLimit, (await QuotaAsync(client)).Remaining);
    }

    [Theory]
    [InlineData("GET", "/ai/conversations", "/conversations")]
    [InlineData("GET", "/ai/conversations/c1", "/conversations/c1")]
    [InlineData("DELETE", "/ai/conversations/c1", "/conversations/c1")]
    public async Task Conversation_routes_without_an_establishment_carry_no_establishment_headers(
        string method,
        string path,
        string forwarded
    )
    {
        using var client = Client(await NewTenantAsync());
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Add(InternalHeaders.EstablishmentId, Guid.NewGuid().ToString());
        request.Headers.Add(InternalHeaders.Jurisdiction, "US");

        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echo = await response.Content.ReadFromJsonAsync<Echo>(Ct);
        Assert.Equal(forwarded, echo!.Path);
        var headers = new Dictionary<string, string>(
            echo.Headers,
            StringComparer.OrdinalIgnoreCase
        );
        Assert.False(headers.ContainsKey(InternalHeaders.EstablishmentId));
        Assert.False(headers.ContainsKey(InternalHeaders.Jurisdiction));
        Assert.False(response.Headers.Contains("RateLimit-Limit"));
    }

    [Fact]
    public async Task The_21st_message_of_a_demo_user_gets_429_without_reaching_the_AI_service()
    {
        var tenant = await NewTenantAsync();
        var establishment = await NewEstablishmentAsync(tenant);
        using var client = Client(tenant);

        for (var sent = 1; sent <= DemoLimit; sent++)
        {
            using var accepted = await SendMessageAsync(client, establishment);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            Assert.Equal(
                (DemoLimit, DemoLimit - sent, 3600),
                (
                    Header(accepted, "RateLimit-Limit"),
                    Header(accepted, "RateLimit-Remaining"),
                    Header(accepted, "RateLimit-Reset")
                )
            );
        }
        using var response = await SendMessageAsync(client, establishment);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(TimeSpan.FromHours(1), response.Headers.RetryAfter!.Delta);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct);
        Assert.Equal("chat.quota_exceeded", problem!.Extensions["code"]!.ToString());
        Assert.Equal(DemoLimit, hits);
    }

    [Fact]
    public async Task Concurrent_messages_never_pass_the_limit_or_lose_a_count()
    {
        var tenant = await NewTenantAsync();
        var establishment = await NewEstablishmentAsync(tenant);
        using var client = Client(tenant);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, DemoLimit + 10).Select(_ => SendMessageAsync(client, establishment))
        );

        Assert.Equal(DemoLimit, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(10, responses.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
        Assert.Equal(DemoLimit, hits);
        Assert.Equal(0, (await QuotaAsync(client)).Remaining);
        foreach (var response in responses)
        {
            response.Dispose();
        }
    }

    [Fact]
    public async Task An_unlimited_tenant_is_never_limited()
    {
        await NewTenantAsync(unlimitedTenant);
        var establishment = await NewEstablishmentAsync(unlimitedTenant);
        using var client = Client(unlimitedTenant);

        for (var sent = 1; sent <= DemoLimit + 5; sent++)
        {
            using var response = await SendMessageAsync(client, establishment);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.False(response.Headers.Contains("RateLimit-Limit"));
        }

        Assert.Equal(new Quota(null, null, Start.AddHours(1)), await QuotaAsync(client));
    }

    [Fact]
    public async Task Users_and_tenants_are_counted_separately()
    {
        var (tenantA, tenantB) = (await NewTenantAsync(), await NewTenantAsync());
        var (establishmentA, establishmentB) = (
            await NewEstablishmentAsync(tenantA),
            await NewEstablishmentAsync(tenantB)
        );
        using var user = Client(tenantA, "user-1");
        await ExhaustAsync(user, establishmentA);

        using var sameTenantOtherUser = Client(tenantA, "user-2");
        using var sameUserOtherTenant = Client(tenantB, "user-1");
        using var other = await SendMessageAsync(sameTenantOtherUser, establishmentA);
        using var elsewhere = await SendMessageAsync(sameUserOtherTenant, establishmentB);

        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        Assert.Equal(DemoLimit - 1, Header(other, "RateLimit-Remaining"));
        Assert.Equal(HttpStatusCode.OK, elsewhere.StatusCode);
        Assert.Equal(DemoLimit - 1, Header(elsewhere, "RateLimit-Remaining"));
    }

    [Fact]
    public async Task Counts_reset_on_the_next_UTC_day()
    {
        var tenant = await NewTenantAsync();
        var establishment = await NewEstablishmentAsync(tenant);
        using var client = Client(tenant);
        await ExhaustAsync(client, establishment);

        clock.Now = Start.AddHours(1); // midnight UTC
        using var response = await SendMessageAsync(client, establishment);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DemoLimit - 1, Header(response, "RateLimit-Remaining"));
        Assert.Equal(24 * 3600, Header(response, "RateLimit-Reset"));
    }

    [Fact]
    public async Task A_message_the_AI_service_fails_on_still_counts()
    {
        var tenant = await NewTenantAsync();
        var establishment = await NewEstablishmentAsync(tenant);
        using var client = Client(tenant);

        using var response = await client.PostAsync(
            $"/ai/establishments/{establishment}/conversations/fail/messages",
            null,
            Ct
        );

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(DemoLimit - 1, Header(response, "RateLimit-Remaining"));
        Assert.Equal(DemoLimit - 1, (await QuotaAsync(client)).Remaining);
    }

    [Fact]
    public async Task The_quota_endpoint_shows_what_is_left_today()
    {
        var tenant = await NewTenantAsync();
        var establishment = await NewEstablishmentAsync(tenant);
        using var client = Client(tenant);
        var resetsAt = new DateTimeOffset(2031, 3, 15, 0, 0, 0, TimeSpan.Zero);

        var before = await QuotaAsync(client);
        (await SendMessageAsync(client, establishment)).Dispose();
        var after = await QuotaAsync(client);

        Assert.Equal(new Quota(DemoLimit, DemoLimit, resetsAt), before);
        Assert.Equal(new Quota(DemoLimit, DemoLimit - 1, resetsAt), after);
    }

    private async Task<Guid> NewTenantAsync(Guid? id = null)
    {
        var tenant = id ?? Guid.NewGuid();
        await using var session = await Session.BeginAsync(db.ConnectionString("app_core"), tenant);
        await session.ExecuteAsync(
            "INSERT INTO core.tenants (id, name) VALUES ($1, 'Tenant')",
            tenant
        );
        await session.CommitAsync();
        return tenant;
    }

    private async Task<Guid> NewEstablishmentAsync(Guid tenant, string jurisdiction = "BR")
    {
        var establishment = Guid.CreateVersion7();
        await using var session = await Session.BeginAsync(db.ConnectionString("app_core"), tenant);
        await session.ExecuteAsync(
            """
            INSERT INTO core.establishments (id, tenant_id, name, type, jurisdiction)
            VALUES ($1, $2, 'Kitchen', 'Restaurant', $3)
            """,
            establishment,
            tenant,
            jurisdiction
        );
        await session.CommitAsync();
        return establishment;
    }

    private HttpClient Client(Guid tenant, string user = ApiFactory.UserId) =>
        Api.CreateClient(
            ApiFactory.CreateToken(token =>
            {
                token.Claims["tenant_id"] = tenant.ToString();
                token.Claims["sub"] = user;
            })
        );

    private static Task<HttpResponseMessage> SendMessageAsync(
        HttpClient client,
        Guid establishment
    ) =>
        client.PostAsJsonAsync(
            $"/ai/establishments/{establishment}/conversations/c1/messages",
            new { content = "Which temperature for hot holding?" },
            Ct
        );

    /// <summary>Sends the whole daily allowance, then checks the next message is refused.</summary>
    private static async Task ExhaustAsync(HttpClient client, Guid establishment)
    {
        for (var sent = 0; sent < DemoLimit; sent++)
        {
            using var accepted = await SendMessageAsync(client, establishment);
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        }
        using var refused = await SendMessageAsync(client, establishment);
        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
    }

    private static async Task<Quota> QuotaAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<Quota>("/chat/quota", Ct))!;

    private static int Header(HttpResponseMessage response, string name) =>
        int.Parse(response.Headers.GetValues(name).Single(), CultureInfo.InvariantCulture);

    private sealed record Echo(string Path, Dictionary<string, string> Headers);

    private sealed record Quota(int? Limit, int? Remaining, DateTimeOffset ResetsAt);

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; }

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
