using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Normetry.Application.Documents;
using Normetry.Infrastructure.Messaging;
using Normetry.IntegrationTests.Database;
using Xunit;

namespace Normetry.IntegrationTests.Documents;

/// <summary>
/// Upload, new version, list, get and delete through the API, on the test database. Each test
/// makes its own tenants, so tests never see each other's documents.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DocumentEndpointTests
{
    private readonly DatabaseFixture db;

    public DocumentEndpointTests(DatabaseFixture db)
    {
        db.SkipWithoutDocker();
        this.db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Tenant(Guid Id, Guid Establishment, string Owner);

    private const string PastDate = "2020-01-31";

    [Fact]
    public async Task An_upload_answers_202_stores_the_file_under_its_tenant_and_announces_it()
    {
        var tenant = await NewTenantAsync();
        using var client = Client(tenant);

        using var response = await UploadAsync(
            client,
            tenant.Establishment,
            "Cold storage SOP.pdf",
            "application/pdf",
            SampleFiles.Pdf
        );

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var document = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var id = document.GetProperty("id").GetGuid();
        Assert.Equal($"/documents/{id}", response.Headers.Location!.OriginalString);
        Assert.Equal(tenant.Establishment, document.GetProperty("establishmentId").GetGuid());
        Assert.Equal("Cold storage SOP", document.GetProperty("title").GetString());
        Assert.Equal("Sop", document.GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Null, document.GetProperty("currentVersionNumber").ValueKind);
        var version = Assert.Single(document.GetProperty("versions").EnumerateArray());
        Assert.Equal(1, version.GetProperty("number").GetInt32());
        Assert.Equal("Uploaded", version.GetProperty("status").GetString());
        Assert.Equal("Cold storage SOP.pdf", version.GetProperty("fileName").GetString());
        Assert.Equal(SampleFiles.Pdf.Length, version.GetProperty("size").GetInt64());
        Assert.Equal(
            "Owner",
            version.GetProperty("uploadedBy").GetProperty("displayName").GetString()
        );

        // The path is built from the token's tenant and the server's ids; the name is fixed.
        var versionId = version.GetProperty("id").GetGuid();
        var path =
            $"tenants/{tenant.Id}/establishments/{tenant.Establishment}/documents/{id}/versions/{versionId}/file.pdf";
        Assert.Equal(SampleFiles.Pdf, db.Api.Blobs.Blobs[path].Content);
        Assert.Equal("application/pdf", db.Api.Blobs.Blobs[path].ContentType);

        var message = Assert.Single(await OutboxAsync(tenant.Id));
        Assert.Equal(tenant.Id, message.TenantId);
        var uploaded = Assert.IsType<DocumentUploaded>(message.Payload);
        Assert.Equal(
            new DocumentUploaded(
                tenant.Establishment,
                id,
                versionId,
                1,
                path,
                "application/pdf",
                SampleFiles.Pdf.Length,
                Convert.ToHexStringLower(
                    System.Security.Cryptography.SHA256.HashData(SampleFiles.Pdf)
                )
            ),
            uploaded
        );
    }

    [Theory]
    [InlineData("sop.pdf", "application/pdf", "Pdf", null)]
    [InlineData("SOP.PDF", "application/pdf", "Pdf", null)]
    [InlineData("manual.docx", SampleFiles.DocxContentType, "Docx", null)]
    [InlineData("notes.txt", "text/plain; charset=utf-8", "Txt", null)]
    [InlineData("temps.csv", "text/csv", "Csv", null)]
    [InlineData("temps.csv", "application/vnd.ms-excel", "Csv", null)]
    [InlineData("sop.pdf", "text/plain", "Pdf", "document.unsupported_format")]
    [InlineData("sop.exe", "application/pdf", "Pdf", "document.unsupported_format")]
    [InlineData("scan.png", "image/png", "Pdf", "document.unsupported_format")]
    [InlineData("sop.pdf", "application/pdf", "Txt", "document.invalid_content")]
    [InlineData("manual.docx", SampleFiles.DocxContentType, "PlainZip", "document.invalid_content")]
    [InlineData("notes.txt", "text/plain", "Pdf", "document.invalid_content")]
    [InlineData("notes.txt", "text/plain", "Binary", "document.invalid_content")]
    [InlineData("notes.txt", "text/plain", "Empty", "document.empty")]
    [InlineData("big.txt", "text/plain", "TooLarge", "document.too_large")]
    public async Task Files_are_checked_by_extension_content_type_and_content(
        string fileName,
        string contentType,
        string content,
        string? refusal
    )
    {
        var tenant = await NewTenantAsync();
        using var client = Client(tenant);
        var bytes = content switch
        {
            "Pdf" => SampleFiles.Pdf,
            "Docx" => SampleFiles.Docx,
            "PlainZip" => SampleFiles.PlainZip,
            "Txt" => SampleFiles.Txt,
            "Csv" => SampleFiles.Csv,
            "Binary" => [0x41, 0x00, 0x42],
            "Empty" => [],
            _ => Enumerable.Repeat((byte)'a', (int)DocumentFiles.MaxSize + 1).ToArray(),
        };

        using var response = await UploadAsync(
            client,
            tenant.Establishment,
            fileName,
            contentType,
            bytes
        );

        if (refusal is null)
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
            return;
        }
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(refusal, await CodeAsync(response));
        Assert.Empty(await OutboxAsync(tenant.Id));
    }

    [Fact]
    public async Task An_upload_without_a_file_title_or_known_type_is_a_validation_problem()
    {
        var tenant = await NewTenantAsync();
        using var client = Client(tenant);
        using var form = new MultipartFormDataContent { { new StringContent("1"), "type" } };

        using var response = await client.PostAsync(
            $"/establishments/{tenant.Establishment}/documents",
            form,
            Ct
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("validation", problem.GetProperty("code").GetString());
        var fields = problem.GetProperty("errors").EnumerateObject().Select(field => field.Name);
        Assert.Equal(["File", "Title", "Type"], fields.Order());
    }

    [Fact]
    public async Task A_new_version_is_refused_while_the_newest_is_in_flight()
    {
        var tenant = await NewTenantAsync();
        using var client = Client(tenant);
        var (document, first) = await UploadPdfAsync(client, tenant);

        // Uploaded, then Processing: both refuse a new version.
        foreach (var status in new[] { "Uploaded", "Processing" })
        {
            await SetStatusAsync(tenant.Id, first, status);
            using var refused = await UploadVersionAsync(client, document);
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            Assert.Equal("document.version_in_flight", await CodeAsync(refused));
        }

        await SetStatusAsync(tenant.Id, first, "Indexed");
        using var accepted = await UploadVersionAsync(client, document);

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var body = await accepted.Content.ReadFromJsonAsync<JsonElement>(Ct);
        // The new version waits; version 1 stays current until version 2 is indexed.
        Assert.Equal(1, body.GetProperty("currentVersionNumber").GetInt32());
        Assert.Equal(
            [(2, "Uploaded"), (1, "Indexed")],
            body.GetProperty("versions")
                .EnumerateArray()
                .Select(version =>
                    (
                        version.GetProperty("number").GetInt32(),
                        version.GetProperty("status").GetString()
                    )
                )
        );
        Assert.Equal(2, (await OutboxAsync(tenant.Id)).Count);

        // A failed newest version allows the next one too.
        await SetStatusAsync(
            tenant.Id,
            body.GetProperty("versions")[0].GetProperty("id").GetGuid(),
            "Failed"
        );
        using var third = await UploadVersionAsync(client, document);
        Assert.Equal(HttpStatusCode.Accepted, third.StatusCode);
    }

    [Fact]
    public async Task A_document_holds_at_most_the_configured_number_of_versions()
    {
        var tenant = await NewTenantAsync();
        await using var api = db.Api.WithWebHostBuilder(builder =>
            builder.UseSetting("Documents:MaxVersionsPerDocument", "1")
        );
        using var client = api.CreateClient(
            ApiFactory.CreateToken(tenant.Id.ToString(), tenant.Owner)
        );
        var (document, first) = await UploadPdfAsync(client, tenant);
        await SetStatusAsync(tenant.Id, first, "Indexed");

        using var refused = await UploadVersionAsync(client, document);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("document.version_limit_reached", await CodeAsync(refused));
    }

    [Fact]
    public async Task An_establishment_holds_at_most_the_configured_number_of_documents()
    {
        var tenant = await NewTenantAsync();
        await using var api = db.Api.WithWebHostBuilder(builder =>
            builder.UseSetting("Documents:MaxDocumentsPerEstablishment", "1")
        );
        using var client = api.CreateClient(
            ApiFactory.CreateToken(tenant.Id.ToString(), tenant.Owner)
        );

        using var first = await UploadAsync(
            client,
            tenant.Establishment,
            "a.pdf",
            "application/pdf",
            SampleFiles.Pdf
        );
        using var second = await UploadAsync(
            client,
            tenant.Establishment,
            "b.pdf",
            "application/pdf",
            SampleFiles.Pdf
        );

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("document.limit_reached", await CodeAsync(second));

        // A deleted document frees its place.
        var id = (await first.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("id")
            .GetGuid();
        using var deleted = await client.DeleteAsync($"/documents/{id}", Ct);
        using var third = await UploadAsync(
            client,
            tenant.Establishment,
            "c.pdf",
            "application/pdf",
            SampleFiles.Pdf
        );
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, third.StatusCode);
    }

    [Fact]
    public async Task The_list_shows_each_document_with_its_newest_version_most_recently_updated_first()
    {
        var tenant = await NewTenantAsync();
        using var client = Client(tenant);
        var (older, olderVersion) = await UploadPdfAsync(client, tenant, "Older");
        await SetStatusAsync(tenant.Id, olderVersion, "Indexed");
        using (await UploadVersionAsync(client, older)) { }
        var (newer, _) = await UploadPdfAsync(client, tenant, "Newer");

        var list = await client.GetFromJsonAsync<JsonElement>(
            $"/establishments/{tenant.Establishment}/documents",
            Ct
        );

        Assert.Equal(
            [newer, older],
            list.EnumerateArray().Select(item => item.GetProperty("id").GetGuid())
        );
        var olderItem = list[1];
        Assert.Equal("Older", olderItem.GetProperty("title").GetString());
        Assert.Equal(1, olderItem.GetProperty("currentVersionNumber").GetInt32());
        Assert.Equal(2, olderItem.GetProperty("latestVersion").GetProperty("number").GetInt32());
        Assert.Equal(
            "Uploaded",
            olderItem.GetProperty("latestVersion").GetProperty("status").GetString()
        );
        Assert.Equal(
            "Owner",
            olderItem
                .GetProperty("latestVersion")
                .GetProperty("uploadedBy")
                .GetProperty("displayName")
                .GetString()
        );

        var detail = await client.GetFromJsonAsync<JsonElement>($"/documents/{older}", Ct);
        Assert.Equal(
            [2, 1],
            detail
                .GetProperty("versions")
                .EnumerateArray()
                .Select(version => version.GetProperty("number").GetInt32())
        );
    }

    [Fact]
    public async Task Another_tenants_establishment_and_documents_are_not_found()
    {
        var a = await NewTenantAsync();
        var b = await NewTenantAsync();
        using var clientA = Client(a);
        using var clientB = Client(b);
        var (document, _) = await UploadPdfAsync(clientA, a);
        var blobs = db.Api.Blobs.Blobs.Count;

        using var upload = await UploadAsync(
            clientB,
            a.Establishment,
            "x.pdf",
            "application/pdf",
            SampleFiles.Pdf
        );
        using var list = await clientB.GetAsync($"/establishments/{a.Establishment}/documents", Ct);
        using var get = await clientB.GetAsync($"/documents/{document}", Ct);
        using var version = await UploadVersionAsync(clientB, document);
        using var delete = await clientB.DeleteAsync($"/documents/{document}", Ct);

        Assert.Equal(
            [
                HttpStatusCode.NotFound,
                HttpStatusCode.NotFound,
                HttpStatusCode.NotFound,
                HttpStatusCode.NotFound,
                HttpStatusCode.NotFound,
            ],
            new[]
            {
                upload.StatusCode,
                list.StatusCode,
                get.StatusCode,
                version.StatusCode,
                delete.StatusCode,
            }
        );
        Assert.Equal("establishment.not_found", await CodeAsync(upload));
        Assert.Equal("document.not_found", await CodeAsync(get));
        // Nothing was stored for tenant B, and A's document is untouched.
        Assert.Equal(blobs, db.Api.Blobs.Blobs.Count);
        Assert.Empty(await OutboxAsync(b.Id));
        using var stillThere = await clientA.GetAsync($"/documents/{document}", Ct);
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
    }

    [Fact]
    public async Task A_deleted_document_is_gone_from_the_api_and_announced()
    {
        var tenant = await NewTenantAsync();
        using var client = Client(tenant);
        var (document, _) = await UploadPdfAsync(client, tenant);

        using var deleted = await client.DeleteAsync($"/documents/{document}", Ct);
        using var again = await client.DeleteAsync($"/documents/{document}", Ct);
        using var get = await client.GetAsync($"/documents/{document}", Ct);
        var list = await client.GetFromJsonAsync<JsonElement>(
            $"/establishments/{tenant.Establishment}/documents",
            Ct
        );

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Empty(list.EnumerateArray());
        Assert.Equal(
            new DocumentDeleted(tenant.Establishment, document),
            (await OutboxAsync(tenant.Id)).Last().Payload
        );
    }

    [Fact]
    public async Task An_expiry_date_is_set_at_upload_then_changed_and_cleared()
    {
        var tenant = await NewTenantAsync();
        using var client = Client(tenant);

        // A past date is allowed: the document is expired.
        var (document, _) = await UploadPdfAsync(client, tenant.Establishment, "License", PastDate);
        Assert.Equal(PastDate, await ExpiresOnAsync(client, document));

        using var changed = await SetExpiryAsync(
            client,
            tenant.Establishment,
            document,
            "2027-06-30"
        );
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Equal("2027-06-30", await ExpiresOnAsync(client, document));
        var list = await client.GetFromJsonAsync<JsonElement>(
            $"/establishments/{tenant.Establishment}/documents",
            Ct
        );
        Assert.Equal("2027-06-30", list[0].GetProperty("expiresOn").GetString());

        using var cleared = await SetExpiryAsync(client, tenant.Establishment, document, null);
        Assert.Equal(HttpStatusCode.NoContent, cleared.StatusCode);
        Assert.Null(await ExpiresOnAsync(client, document));
        // The date is the core's alone: only the upload was announced.
        Assert.Single(await OutboxAsync(tenant.Id));
    }

    [Fact]
    public async Task An_expiry_date_that_is_not_an_iso_date_is_a_400()
    {
        var tenant = await NewTenantAsync();
        using var client = Client(tenant);

        using var upload = await UploadAsync(
            client,
            tenant.Establishment,
            "license.pdf",
            "application/pdf",
            SampleFiles.Pdf,
            expiresOn: "31/12/2026"
        );

        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
        var problem = await upload.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("validation", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("ExpiresOn", out _));
        Assert.Empty(await OutboxAsync(tenant.Id));

        // A PATCH body needs the field, holding an ISO date or null.
        var (document, _) = await UploadPdfAsync(client, tenant.Establishment);
        foreach (
            var body in new[]
            {
                "{\"expiresOn\":\"2026-13-01\"}",
                "{\"expiresOn\":\"31/12/2026\"}",
                "{}",
            }
        )
        {
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var refused = await client.PatchAsync(
                $"/establishments/{tenant.Establishment}/documents/{document}",
                content,
                Ct
            );
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        }
        Assert.Null(await ExpiresOnAsync(client, document));
    }

    [Fact]
    public async Task The_list_filters_expired_documents_and_documents_expiring_within_n_days()
    {
        var tenant = await NewTenantAsync();
        var today = new DateOnly(2030, 6, 15);
        await using var api = db.Api.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<TimeProvider>(new FixedClock(today))
            )
        );
        using var client = api.CreateClient(
            ApiFactory.CreateToken(tenant.Id.ToString(), tenant.Owner)
        );
        foreach (
            var (title, days) in new (string, int?)[]
            {
                ("Yesterday", -1),
                ("Today", 0),
                ("In 10 days", 10),
                ("In 11 days", 11),
                ("In 90 days", 90),
                ("No date", null),
            }
        )
        {
            await UploadPdfAsync(
                client,
                tenant.Establishment,
                title,
                days is { } offset ? today.AddDays(offset).ToString("yyyy-MM-dd") : null
            );
        }

        Assert.Equal(
            ["Yesterday"],
            await TitlesAsync(client, tenant.Establishment, "expired=true")
        );
        Assert.Equal(
            ["In 10 days", "Today"],
            await TitlesAsync(client, tenant.Establishment, "expiringWithinDays=10")
        );
        Assert.Equal(
            ["In 10 days", "In 11 days", "In 90 days", "Today"],
            await TitlesAsync(client, tenant.Establishment, "expiringWithinDays=90")
        );
        Assert.Equal(6, (await TitlesAsync(client, tenant.Establishment, "expired=false")).Count);

        foreach (
            var query in new[]
            {
                "expiringWithinDays=0",
                "expiringWithinDays=366",
                "expired=true&expiringWithinDays=30",
            }
        )
        {
            using var refused = await client.GetAsync(
                $"/establishments/{tenant.Establishment}/documents?{query}",
                Ct
            );
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Equal("validation", await CodeAsync(refused));
        }
    }

    [Fact]
    public async Task Expiry_filters_and_changes_stay_within_the_tenant_and_the_establishment()
    {
        var a = await NewTenantAsync();
        var b = await NewTenantAsync();
        var otherEstablishment = await TestTenants.CreateEstablishmentAsync(db, a.Id);
        using var clientA = Client(a);
        using var clientB = Client(b);
        var (document, _) = await UploadPdfAsync(clientA, a.Establishment, "A", PastDate);
        var (elsewhere, _) = await UploadPdfAsync(clientA, otherEstablishment, "A other", PastDate);
        var (deleted, _) = await UploadPdfAsync(clientA, a.Establishment, "A deleted", PastDate);
        using (await clientA.DeleteAsync($"/documents/{deleted}", Ct)) { }
        await UploadPdfAsync(clientB, b.Establishment, "B", PastDate);

        // A filtered list holds its own tenant's and establishment's documents only.
        Assert.Equal(["A"], await TitlesAsync(clientA, a.Establishment, "expired=true"));
        Assert.Equal(["A other"], await TitlesAsync(clientA, otherEstablishment, "expired=true"));
        Assert.Equal(["B"], await TitlesAsync(clientB, b.Establishment, "expired=true"));
        using var foreignList = await clientB.GetAsync(
            $"/establishments/{a.Establishment}/documents?expired=true",
            Ct
        );
        Assert.Equal(HttpStatusCode.NotFound, foreignList.StatusCode);

        // Another tenant's document (under its own or the caller's establishment), a document of
        // another establishment and a deleted document are not found.
        using var foreign = await SetExpiryAsync(clientB, a.Establishment, document, null);
        using var foreignHere = await SetExpiryAsync(clientB, b.Establishment, document, null);
        using var wrongEstablishment = await SetExpiryAsync(
            clientA,
            a.Establishment,
            elsewhere,
            null
        );
        using var gone = await SetExpiryAsync(clientA, a.Establishment, deleted, null);
        foreach (var response in new[] { foreign, foreignHere, wrongEstablishment, gone })
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("document.not_found", await CodeAsync(response));
        }
        await using var session = await Session.BeginAsync(db.ConnectionString("app_core"), a.Id);
        Assert.Equal(
            [PastDate, PastDate, PastDate],
            await session.ColumnAsync<string>("SELECT expires_on::text FROM core.documents")
        );
    }

    private async Task<Tenant> NewTenantAsync()
    {
        var owner = Guid.NewGuid().ToString();
        var tenant = await TestTenants.CreateAsync(db, null, owner);
        await using (
            var session = await Session.BeginAsync(db.ConnectionString("app_core"), tenant)
        )
        {
            await session.ExecuteAsync(
                "UPDATE core.users SET display_name = 'Owner' WHERE sub = $1",
                owner
            );
            await session.CommitAsync();
        }
        return new Tenant(tenant, await TestTenants.CreateEstablishmentAsync(db, tenant), owner);
    }

    private HttpClient Client(Tenant tenant) =>
        db.Api.CreateClient(ApiFactory.CreateToken(tenant.Id.ToString(), tenant.Owner));

    internal static async Task<HttpResponseMessage> UploadAsync(
        HttpClient client,
        Guid establishment,
        string fileName,
        string contentType,
        byte[] content,
        string title = "Cold storage SOP",
        string type = "Sop",
        string? expiresOn = null
    )
    {
        using var form = new MultipartFormDataContent
        {
            { File(content, contentType), "file", fileName },
            { new StringContent(title), "title" },
            { new StringContent(type), "type" },
        };
        if (expiresOn is not null)
        {
            form.Add(new StringContent(expiresOn), "expiresOn");
        }
        return await client.PostAsync($"/establishments/{establishment}/documents", form, Ct);
    }

    internal static async Task<HttpResponseMessage> UploadVersionAsync(
        HttpClient client,
        Guid document
    )
    {
        using var form = new MultipartFormDataContent
        {
            { File(SampleFiles.Pdf, "application/pdf"), "file", "sop-v2.pdf" },
        };
        return await client.PostAsync($"/documents/{document}/versions", form, Ct);
    }

    /// <summary>Uploads a PDF and returns the document and version ids.</summary>
    internal static async Task<(Guid Document, Guid Version)> UploadPdfAsync(
        HttpClient client,
        Guid establishment,
        string title = "Cold storage SOP",
        string? expiresOn = null
    )
    {
        using var response = await UploadAsync(
            client,
            establishment,
            "sop.pdf",
            "application/pdf",
            SampleFiles.Pdf,
            title,
            expiresOn: expiresOn
        );
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return (
            body.GetProperty("id").GetGuid(),
            body.GetProperty("versions")[0].GetProperty("id").GetGuid()
        );
    }

    private static Task<(Guid Document, Guid Version)> UploadPdfAsync(
        HttpClient client,
        Tenant tenant,
        string title = "Cold storage SOP"
    ) => UploadPdfAsync(client, tenant.Establishment, title);

    private static Task<HttpResponseMessage> SetExpiryAsync(
        HttpClient client,
        Guid establishment,
        Guid document,
        string? expiresOn
    ) =>
        client.PatchAsync(
            $"/establishments/{establishment}/documents/{document}",
            JsonContent.Create(new { expiresOn }),
            Ct
        );

    private static async Task<string?> ExpiresOnAsync(HttpClient client, Guid document) =>
        (await client.GetFromJsonAsync<JsonElement>($"/documents/{document}", Ct))
            .GetProperty("expiresOn")
            .GetString();

    /// <summary>The titles a list request answers with, sorted.</summary>
    private static async Task<List<string>> TitlesAsync(
        HttpClient client,
        Guid establishment,
        string query
    ) =>
        [
            .. (
                await client.GetFromJsonAsync<JsonElement>(
                    $"/establishments/{establishment}/documents?{query}",
                    Ct
                )
            )
                .EnumerateArray()
                .Select(item => item.GetProperty("title").GetString()!)
                .Order(StringComparer.Ordinal),
        ];

    private static ByteArrayContent File(byte[] content, string contentType)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return file;
    }

    private async Task SetStatusAsync(Guid tenant, Guid version, string status)
    {
        await using var session = await Session.BeginAsync(db.ConnectionString("app_core"), tenant);
        await session.ExecuteAsync(
            "UPDATE core.document_versions SET status = $2, status_changed_at = now() WHERE id = $1",
            version,
            status
        );
        await session.CommitAsync();
    }

    /// <summary>The tenant's outbox messages, oldest first, as the dispatcher would publish them.</summary>
    private async Task<List<InboundMessage>> OutboxAsync(Guid tenant)
    {
        await using var session = await Session.BeginAsync(db.ConnectionString("app_core"), tenant);
        var bodies = await session.ColumnAsync<string>(
            "SELECT body::text FROM core.outbox_messages ORDER BY created_at, id"
        );
        return
        [
            .. bodies.Select(body => MessageJson.Parse(System.Text.Encoding.UTF8.GetBytes(body))),
        ];
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("code").GetString();

    private sealed class FixedClock(DateOnly today) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            new(today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
    }
}
