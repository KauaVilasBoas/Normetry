using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Normetry.Api.Gateway;
using Normetry.IntegrationTests.Database;
using Xunit;

namespace Normetry.IntegrationTests.Gateway;

/// <summary>
/// Establishment routes against the test database as app_core and a stub AI service that echoes
/// what it received. Every test makes its own tenants and establishments.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ChatGatewayTests : IAsyncLifetime
{
    private readonly DatabaseFixture db;
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

        api = db.Api.WithWebHostBuilder(host => host.UseSetting("Services:ai:http:0", address));
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

    private sealed record Echo(string Path, Dictionary<string, string> Headers);
}
