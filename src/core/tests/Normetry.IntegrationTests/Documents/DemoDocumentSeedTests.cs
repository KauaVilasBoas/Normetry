using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Normetry.Infrastructure.Persistence;
using Normetry.IntegrationTests.Database;
using Xunit;

namespace Normetry.IntegrationTests.Documents;

/// <summary>
/// The development document seed: it uploads through the upload commands, as the tenant with the
/// Owner as uploader, skips a file its document already holds, and never runs outside
/// Development. Each test seeds its own tenant from its own folder, never the demo tenants.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DemoDocumentSeedTests : IDisposable
{
    private readonly DatabaseFixture db;
    private readonly DirectoryInfo folder = Directory.CreateTempSubdirectory(
        "normetry-demo-documents-"
    );

    public DemoDocumentSeedTests(DatabaseFixture db)
    {
        db.SkipWithoutDocker();
        this.db = db;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Documents => Path.Combine(folder.FullName, "kitchen", "documents");

    public void Dispose() => folder.Delete(recursive: true);

    [Fact]
    public async Task Each_listed_document_is_uploaded_once_and_a_changed_file_becomes_a_new_version()
    {
        var sub = Guid.NewGuid().ToString();
        var tenant = await TestTenants.CreateAsync(db, owners: sub);
        await TestTenants.CreateEstablishmentAsync(db, tenant); // named Kitchen
        Directory.CreateDirectory(Documents);
        await File.WriteAllTextAsync(
            Path.Combine(Documents, "manifest.json"),
            """
            {
              "establishment": "Kitchen",
              "documents": [
                { "title": "SOP", "type": "Sop", "file": "sop.txt" },
                { "title": "Log", "type": "Log", "file": "log.csv" }
              ]
            }
            """,
            Ct
        );
        await WriteAsync("sop.txt", "Wash hands before work.");
        await WriteAsync("log.csv", "time,temperature\n08:00,3.1\n");
        DemoDocumentSeed.DemoEstablishment[] targets = [new(tenant, "Kitchen", sub)];
        var seed = NewSeed();

        Assert.Equal(2, await seed.SeedAsync(folder.FullName, targets, Ct));
        Assert.Equal([$"Log Log 1 {sub}", $"SOP Sop 1 {sub}"], await VersionsAsync(tenant));

        // Every skip below is checked with no version in flight, where the version command would
        // otherwise accept a new version.
        // The same files once indexed: each document already holds them.
        await SetStatusAsync(tenant, "Indexed", number: 1);
        Assert.Equal(0, await seed.SeedAsync(folder.FullName, targets, Ct));

        // A changed file becomes version 2.
        await WriteAsync("sop.txt", "Wash hands before work and after every break.");
        Assert.Equal(1, await seed.SeedAsync(folder.FullName, targets, Ct));

        // Version 2 failed: the newest version holds the file, so it is not uploaded again.
        await SetStatusAsync(tenant, "Failed", number: 2);
        Assert.Equal(0, await seed.SeedAsync(folder.FullName, targets, Ct));

        // The file went back to what version 1 holds: the current version matches.
        await WriteAsync("sop.txt", "Wash hands before work.");
        Assert.Equal(0, await seed.SeedAsync(folder.FullName, targets, Ct));

        Assert.Equal(
            [$"Log Log 1 {sub}", $"SOP Sop 1 {sub}", $"SOP Sop 2 {sub}"],
            await VersionsAsync(tenant)
        );
        await using var session = await Session.BeginAsync(db.ConnectionString("app_core"), tenant);
        Assert.Equal(
            3L,
            await session.ScalarAsync<long>(
                "SELECT count(*) FROM core.outbox_messages WHERE type = 'DocumentUploaded'"
            )
        );
    }

    [Fact]
    public async Task The_placeholder_documents_pass_the_upload_commands()
    {
        // evals/establishments, copied next to the tests, uploaded into a test tenant that has
        // establishments named like the demo ones.
        var directory = Path.Combine(AppContext.BaseDirectory, "demo-documents");
        var sub = Guid.NewGuid().ToString();
        var tenant = await TestTenants.CreateAsync(db, owners: sub);
        await using (
            var session = await Session.BeginAsync(db.ConnectionString("app_core"), tenant)
        )
        {
            foreach (var establishment in DemoDocumentSeed.Establishments)
            {
                await session.ExecuteAsync(
                    """
                    INSERT INTO core.establishments (id, tenant_id, name, type, jurisdiction)
                    VALUES ($1, $2, $3, 'Restaurant', 'US')
                    """,
                    Guid.CreateVersion7(),
                    tenant,
                    establishment.Name
                );
            }
            await session.CommitAsync();
        }
        DemoDocumentSeed.DemoEstablishment[] targets =
        [
            .. DemoDocumentSeed.Establishments.Select(establishment =>
                establishment with
                {
                    TenantId = tenant,
                    OwnerSub = sub,
                }
            ),
        ];
        var listed = Directory
            .EnumerateFiles(directory, "manifest.json", SearchOption.AllDirectories)
            .Sum(path =>
                JsonDocument
                    .Parse(File.ReadAllText(path))
                    .RootElement.GetProperty("documents")
                    .GetArrayLength()
            );

        // Every listed file is accepted, so none was refused and logged instead.
        Assert.Equal(listed, await NewSeed().SeedAsync(directory, targets, Ct));
        await using var check = await Session.BeginAsync(db.ConnectionString("app_core"), tenant);
        Assert.Equal(
            DemoDocumentSeed.Establishments.Select(establishment => establishment.Name).Order(),
            await check.ColumnAsync<string>(
                """
                SELECT DISTINCT e.name FROM core.establishments e
                JOIN core.documents d ON d.tenant_id = e.tenant_id AND d.establishment_id = e.id
                ORDER BY 1
                """
            )
        );
    }

    [Fact]
    public async Task The_seed_refuses_to_start_outside_Development()
    {
        var seed = ActivatorUtilities.CreateInstance<DemoDocumentSeed>(
            db.Api.Services,
            new HostingEnvironment { EnvironmentName = Environments.Production }
        );

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => seed.StartAsync(Ct));
        Assert.Contains("only in Development", error.Message);
    }

    [Fact]
    public void The_seed_is_registered_only_in_Development()
    {
        Assert.Single(db.Api.Services.GetServices<IHostedService>().OfType<DemoDocumentSeed>());
        using var production = db.Api.WithWebHostBuilder(builder =>
            builder.UseEnvironment("Production")
        );

        Assert.Empty(production.Services.GetServices<IHostedService>().OfType<DemoDocumentSeed>());
    }

    private DemoDocumentSeed NewSeed() =>
        ActivatorUtilities.CreateInstance<DemoDocumentSeed>(db.Api.Services);

    private Task WriteAsync(string file, string text) =>
        File.WriteAllTextAsync(Path.Combine(Documents, file), text, Ct);

    /// <summary>"title type number uploader-sub" of every version of the tenant.</summary>
    private async Task<List<string>> VersionsAsync(Guid tenant)
    {
        await using var session = await Session.BeginAsync(db.ConnectionString("app_core"), tenant);
        return await session.ColumnAsync<string>(
            """
            SELECT d.title || ' ' || d.type || ' ' || v.number || ' ' || u.sub
            FROM core.documents d
            JOIN core.document_versions v ON v.tenant_id = d.tenant_id AND v.document_id = d.id
            JOIN core.users u ON u.tenant_id = v.tenant_id AND u.id = v.uploaded_by
            ORDER BY 1
            """
        );
    }

    private async Task SetStatusAsync(Guid tenant, string status, int number)
    {
        await using var session = await Session.BeginAsync(db.ConnectionString("app_core"), tenant);
        await session.ExecuteAsync(
            "UPDATE core.document_versions SET status = $1 WHERE number = $2",
            status,
            number
        );
        await session.CommitAsync();
    }
}
