using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Normetry.Api.Auth;
using Normetry.Application.Establishments;
using Normetry.Domain.Establishments;
using Normetry.IntegrationTests.Database;
using Xunit;

namespace Normetry.IntegrationTests.Establishments;

/// <summary>The three establishment endpoints, end to end against the test database as app_core.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class EstablishmentEndpointTests
{
    private readonly ApiFactory api;

    public EstablishmentEndpointTests(DatabaseFixture db)
    {
        db.SkipWithoutDocker();
        api = db.Api;
    }

    // The API sends enums by name.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Client(string tenantId, string role = NormetryRoles.Owner) =>
        api.CreateClient(ApiFactory.CreateToken(tenantId, role));

    private static object Body(string name = "Bistro Centro") =>
        new
        {
            name,
            type = "Restaurant",
            jurisdiction = "BR",
        };

    private async Task<Guid> CreateAsync(string tenantId, string name = "Bistro Centro")
    {
        using var client = Client(tenantId);
        var response = await client.PostAsJsonAsync("/establishments", Body(name), Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Created>(Ct))!.Id;
    }

    private sealed record Created(Guid Id);

    [Theory]
    [InlineData("POST", "/establishments")]
    [InlineData("GET", "/establishments")]
    [InlineData("GET", "/establishments/0190a6e2-7b1c-7d4e-8f2a-3b4c5d6e7f80")]
    public async Task A_token_without_a_tenant_gets_403(string method, string path)
    {
        using var client = api.CreateClient(
            ApiFactory.CreateToken(token => token.Claims.Remove("tenant_id"))
        );
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = method == "POST" ? JsonContent.Create(Body()) : null,
        };

        var response = await client.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(NormetryRoles.Owner, HttpStatusCode.Created)]
    [InlineData(NormetryRoles.Manager, HttpStatusCode.Created)]
    [InlineData(NormetryRoles.Auditor, HttpStatusCode.Forbidden)]
    public async Task Only_an_Owner_or_a_Manager_creates_establishments(
        string role,
        HttpStatusCode expected
    )
    {
        using var client = Client(ApiFactory.TenantA, role);

        var response = await client.PostAsJsonAsync("/establishments", Body(), Ct);

        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task Create_answers_201_with_the_location_of_the_new_establishment()
    {
        using var client = Client(ApiFactory.TenantA);

        var response = await client.PostAsJsonAsync("/establishments", Body("  Bistro Sul  "), Ct);
        var id = (await response.Content.ReadFromJsonAsync<Created>(Ct))!.Id;
        var created = await client.GetFromJsonAsync<EstablishmentResponse>(
            response.Headers.Location,
            Json,
            Ct
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/establishments/{id}", response.Headers.Location!.OriginalString);
        Assert.Equal(
            new EstablishmentResponse(
                id,
                "Bistro Sul",
                EstablishmentType.Restaurant,
                Jurisdiction.BR
            ),
            created
        );
    }

    [Theory]
    [InlineData("""{"name":"","type":"Restaurant","jurisdiction":"US"}""", "Name")]
    [InlineData("""{"name":"Kitchen","jurisdiction":"US"}""", "Type")]
    [InlineData("""{"name":"Kitchen","type":"Restaurant"}""", "Jurisdiction")]
    public async Task Invalid_input_gets_a_validation_problem(string json, string field)
    {
        using var client = Client(ApiFactory.TenantA);

        var response = await client.PostAsync(
            "/establishments",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            Ct
        );
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, problem!.Errors.Keys);
    }

    [Theory]
    [InlineData("""{"name":"Kitchen","type":"Bakery","jurisdiction":"US"}""")]
    [InlineData("""{"name":"Kitchen","type":1,"jurisdiction":"US"}""")]
    [InlineData("""{"name":"Kitchen","type":"Restaurant","jurisdiction":"AR"}""")]
    public async Task Unknown_or_numeric_enum_values_get_400(string json)
    {
        using var client = Client(ApiFactory.TenantA);

        var response = await client.PostAsync(
            "/establishments",
            new StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            Ct
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_tenant_in_the_body_is_ignored()
    {
        using var client = Client(ApiFactory.TenantA);
        var body = new
        {
            name = "Bistro Norte",
            type = "Restaurant",
            jurisdiction = "US",
            tenantId = ApiFactory.TenantB,
        };

        var response = await client.PostAsJsonAsync("/establishments", body, Ct);
        var id = (await response.Content.ReadFromJsonAsync<Created>(Ct))!.Id;
        using var other = Client(ApiFactory.TenantB, NormetryRoles.Auditor);

        Assert.Equal(
            HttpStatusCode.OK,
            (await client.GetAsync($"/establishments/{id}", Ct)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await other.GetAsync($"/establishments/{id}", Ct)).StatusCode
        );
    }

    [Fact]
    public async Task Another_tenants_establishment_is_not_found()
    {
        var id = await CreateAsync(ApiFactory.TenantB);
        using var client = Client(ApiFactory.TenantA, NormetryRoles.Auditor);

        var response = await client.GetAsync($"/establishments/{id}", Ct);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("establishment.not_found", problem!.Extensions["code"]!.ToString());
    }

    [Fact]
    public async Task An_unknown_id_is_not_found()
    {
        using var client = Client(ApiFactory.TenantA, NormetryRoles.Auditor);

        var response = await client.GetAsync($"/establishments/{Guid.CreateVersion7()}", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_list_holds_only_the_callers_tenant_rows()
    {
        var ownA = await CreateAsync(ApiFactory.TenantA, "Bistro Leste");
        var ownB = await CreateAsync(ApiFactory.TenantB, "Bakery Leste");
        using var clientA = Client(ApiFactory.TenantA, NormetryRoles.Auditor);
        using var clientB = Client(ApiFactory.TenantB, NormetryRoles.Auditor);

        var listA = await clientA.GetFromJsonAsync<EstablishmentResponse[]>(
            "/establishments",
            Json,
            Ct
        );
        var listB = await clientB.GetFromJsonAsync<EstablishmentResponse[]>(
            "/establishments",
            Json,
            Ct
        );

        Assert.Contains(listA!, establishment => establishment.Id == ownA);
        Assert.DoesNotContain(listA!, establishment => establishment.Id == ownB);
        Assert.Contains(listB!, establishment => establishment.Id == ownB);
        Assert.DoesNotContain(listB!, establishment => establishment.Id == ownA);
    }
}
