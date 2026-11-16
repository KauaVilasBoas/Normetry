using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
        string type = "Sop"
    )
    {
        using var form = new MultipartFormDataContent
        {
            { File(content, contentType), "file", fileName },
            { new StringContent(title), "title" },
            { new StringContent(type), "type" },
        };
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
        string title = "Cold storage SOP"
    )
    {
        using var response = await UploadAsync(
            client,
            establishment,
            "sop.pdf",
            "application/pdf",
            SampleFiles.Pdf,
            title
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
}
