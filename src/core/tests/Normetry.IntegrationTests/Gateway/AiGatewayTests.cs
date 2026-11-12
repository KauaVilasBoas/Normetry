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
using Xunit;

namespace Normetry.IntegrationTests.Gateway;

/// <summary>
/// <c>/ai/*</c> against a stub AI service on a loopback port, found through the same service
/// discovery setting Aspire injects (<c>Services:ai:http:0</c>). The stub echoes what it received.
/// </summary>
public sealed class AiGatewayTests : IAsyncLifetime
{
    private WebApplication? upstream;
    private WebApplicationFactory<Program>? api;
    private int hits;

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
            // A careless upstream: the gateway must not pass these back to the client.
            response.Headers[InternalHeaders.Token] = "leaked";
            response.Headers["X_Normetry_Debug"] = "leaked";
            return new Echo(
                request.Path.Value!,
                request.QueryString.Value ?? "",
                request.Headers.ToDictionary(
                    header => header.Key,
                    header => header.Value.ToString(),
                    StringComparer.OrdinalIgnoreCase
                )
            );
        });
        await upstream.StartAsync(Ct);
        var address = upstream
            .Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();

        api = new ApiFactory().WithWebHostBuilder(host =>
            host.UseSetting("Services:ai:http:0", address)
        );
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
    public async Task Client_internal_headers_and_credentials_are_replaced_by_values_from_the_token()
    {
        using var client = Api.CreateClient(ApiFactory.CreateToken());
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "/ai/chat/echo?x=1&access_token=core-jwt"
        );
        request.Headers.Add(InternalHeaders.TenantId, ApiFactory.TenantB);
        request.Headers.Add("x-normetry-internal-token", "forged-internal-token-0123456789abcdef");
        request.Headers.Add("X-Normetry-User-Id", "someone-else");
        request.Headers.Add("X-Normetry-Establishment-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X_Normetry_Tenant_Id", ApiFactory.TenantB);
        request.Headers.Add("Cookie", "session=abc");

        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var echo = await response.Content.ReadFromJsonAsync<Echo>(Ct);
        Assert.Equal("/chat/echo", echo!.Path);
        Assert.Equal("?x=1", echo.Query);
        var headers = new Dictionary<string, string>(
            echo.Headers,
            StringComparer.OrdinalIgnoreCase
        );
        // One value each: a client copy would show up as a second, comma-joined value.
        Assert.Equal(ApiFactory.InternalToken, headers[InternalHeaders.Token]);
        Assert.Equal(ApiFactory.TenantA, headers[InternalHeaders.TenantId]);
        Assert.Equal(ApiFactory.UserId, headers[InternalHeaders.UserId]);
        // Nothing else with the prefix, in either spelling: the client's extra headers are gone.
        Assert.Equal(
            new[] { InternalHeaders.TenantId, InternalHeaders.Token, InternalHeaders.UserId }
                .Select(name => name.ToUpperInvariant())
                .Order(),
            headers
                .Keys.Select(name => name.Replace('_', '-').ToUpperInvariant())
                .Where(name => name.StartsWith(InternalHeaders.Prefix.ToUpperInvariant()))
                .Order()
        );
        Assert.False(headers.ContainsKey("Authorization"));
        Assert.False(headers.ContainsKey("Cookie"));
        Assert.DoesNotContain(response.Headers, header =>
            header
                .Key.Replace('_', '-')
                .StartsWith(InternalHeaders.Prefix, StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task A_request_without_a_token_gets_401_and_never_reaches_the_AI_service()
    {
        using var client = Api.CreateClient(accessToken: null);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/ai/health");
        request.Headers.Add(InternalHeaders.Token, ApiFactory.InternalToken);
        request.Headers.Add(InternalHeaders.TenantId, ApiFactory.TenantA);

        using var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, hits);
    }

    [Fact]
    public async Task A_token_without_a_tenant_gets_403_and_never_reaches_the_AI_service()
    {
        using var client = Api.CreateClient(
            ApiFactory.CreateToken(token => token.Claims.Remove("tenant_id"))
        );

        using var response = await client.GetAsync("/ai/health", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, hits);
    }

    [Fact]
    public void The_API_refuses_to_start_without_a_long_enough_internal_token()
    {
        using var factory = new ApiFactory().WithWebHostBuilder(host =>
            host.UseSetting("Ai:InternalToken", "short")
        );

        var error = Assert.Throws<InvalidOperationException>(() => factory.Services);

        Assert.Contains("Ai:InternalToken", error.Message);
    }

    [Fact]
    public void The_API_refuses_to_start_without_the_AI_service_address()
    {
        using var factory = new ApiFactory().WithWebHostBuilder(host =>
            host.UseSetting("Services:ai:http:0", "")
        );

        var error = Assert.Throws<InvalidOperationException>(() => factory.Services);

        Assert.Contains("Services:ai:http", error.Message);
    }

    private sealed record Echo(string Path, string Query, Dictionary<string, string> Headers);
}
