using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Normetry.Application.Documents;
using Normetry.Application.Documents.SetDocumentExpiry;
using Normetry.Application.Documents.UploadDocument;
using Normetry.Application.Documents.UploadDocumentVersion;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.Domain.Documents;
using Normetry.Domain.Identity;

namespace Normetry.Infrastructure.Persistence;

/// <summary>
/// Development only: once the API has started (so the demo tenants exist), uploads the demo
/// establishments' documents listed in <c>evals/establishments/*/documents/manifest.json</c>
/// through the upload commands, as the establishment's tenant with its demo Owner as uploader:
/// blob, version and outbox message, then the AI service ingests them like any upload.
/// Idempotent: a document (matched by title) whose current or newest version already has the
/// file's SHA-256 is skipped, and a changed file becomes a new version. The document's expiry date
/// follows the manifest's <c>expiresOn</c> (none when absent) without a new version. A failure is
/// logged and never stops the API.
/// </summary>
public sealed class DemoDocumentSeed(
    IServiceScopeFactory scopes,
    IHostEnvironment environment,
    IHostApplicationLifetime lifetime,
    IConfiguration configuration,
    ILogger<DemoDocumentSeed> logger
) : BackgroundService
{
    /// <summary>Where the establishment folders are; by default the repository's <c>evals/establishments</c>.</summary>
    public const string DirectorySetting = "DemoDocuments:Directory";

    private static readonly JsonSerializerOptions ManifestJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    /// <summary>Every demo establishment, with its tenant and its Owner's <c>sub</c>.</summary>
    internal static readonly DemoEstablishment[] Establishments =
    [
        .. DemoTenantSeed.Tenants.SelectMany(tenant =>
            tenant.Establishments.Select(establishment => new DemoEstablishment(
                tenant.Id,
                establishment.Name,
                tenant.Users.Single(user => user.SystemKey == Profile.SystemKeys.Owner).Sub
            ))
        ),
    ];

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // Demo data must never reach a deployed database.
        if (!environment.IsDevelopment())
        {
            throw new InvalidOperationException("The demo document seed runs only in Development.");
        }
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var started = new TaskCompletionSource();
        using (lifetime.ApplicationStarted.Register(() => started.TrySetResult()))
        {
            await started.Task.WaitAsync(stoppingToken);
        }
        var directory = RepositoryPaths.Resolve(
            configuration,
            DirectorySetting,
            environment,
            "evals",
            "establishments"
        );
        try
        {
            await SeedAsync(directory, Establishments, stoppingToken);
        }
        catch (Exception error) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogWarning(error, "The demo document seed failed");
        }
    }

    /// <summary>Uploads what the manifests under <paramref name="directory"/> list and returns how many uploads it made.</summary>
    internal async Task<int> SeedAsync(
        string directory,
        IReadOnlyCollection<DemoEstablishment> establishments,
        CancellationToken cancellationToken
    )
    {
        if (!Directory.Exists(directory))
        {
            logger.LogInformation("No demo documents at {Directory}", directory);
            return 0;
        }
        var uploads = 0;
        foreach (
            var path in Directory
                .EnumerateFiles(directory, "manifest.json", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
        )
        {
            try
            {
                uploads += await SeedManifestAsync(path, establishments, cancellationToken);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(error, "Skipping the demo documents of {Manifest}", path);
            }
        }
        return uploads;
    }

    private async Task<int> SeedManifestAsync(
        string path,
        IReadOnlyCollection<DemoEstablishment> establishments,
        CancellationToken cancellationToken
    )
    {
        var manifest = await ReadManifestAsync(path, cancellationToken);
        var target = establishments.SingleOrDefault(establishment =>
            establishment.Name == manifest.Establishment
        )
            ?? throw new InvalidDataException(
                $"'{manifest.Establishment}' is not a demo establishment."
            );

        var (establishmentId, existing) = await ReadAsync(target, cancellationToken);
        var uploads = 0;
        foreach (var entry in manifest.Documents)
        {
            var file = Path.Combine(Path.GetDirectoryName(path)!, entry.File);
            var content = await File.ReadAllBytesAsync(file, cancellationToken);
            var sha256 = Convert.ToHexStringLower(SHA256.HashData(content));
            var document = existing.FirstOrDefault(document => document.Title == entry.Title);
            if (document is not null && document.ExpiresOn != entry.ExpiresOn)
            {
                await SetExpiryAsync(target, establishmentId, document, entry, cancellationToken);
            }
            if (document is not null && IsUnchanged(document, sha256))
            {
                continue;
            }

            var upload = new UploadedFile(
                Path.GetFileName(file),
                DocumentFiles.ContentTypeOf(file),
                content
            );
            if (
                await UploadOrAddVersionAsync(
                    path,
                    target,
                    establishmentId,
                    document,
                    entry,
                    upload,
                    cancellationToken
                )
            )
            {
                uploads++;
            }
        }
        return uploads;
    }

    private static async Task<Manifest> ReadManifestAsync(
        string path,
        CancellationToken cancellationToken
    )
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<Manifest>(
            stream,
            ManifestJson,
            cancellationToken
        ) ?? throw new InvalidDataException("The manifest is empty.");
    }

    /// <summary>The file is already the document's current or newest version.</summary>
    private static bool IsUnchanged(Document document, string sha256) =>
        document.Current?.Sha256 == sha256 || document.Newest.Sha256 == sha256;

    /// <summary>
    /// Uploads <paramref name="upload"/> as a new document, or as a new version of
    /// <paramref name="document"/> when the title exists, and says whether it was uploaded.
    /// </summary>
    private async Task<bool> UploadOrAddVersionAsync(
        string manifestPath,
        DemoEstablishment target,
        Guid establishmentId,
        Document? document,
        ManifestDocument entry,
        UploadedFile upload,
        CancellationToken cancellationToken
    )
    {
        await using var scope = TenantScope.Open(scopes, logger, target.TenantId, target.OwnerSub);
        var dispatcher = scope.Services.GetRequiredService<IDispatcher>();
        var result = document is null
            ? await dispatcher.SendAsync(
                new UploadDocumentCommand(
                    establishmentId,
                    entry.Title,
                    entry.Type,
                    upload,
                    entry.ExpiresOn
                ),
                cancellationToken
            )
            : await dispatcher.SendAsync(
                new UploadDocumentVersionCommand(document.Id, upload),
                cancellationToken
            );
        // Ids and codes only: a title is document data, like the file name.
        if (!result.IsSuccess)
        {
            logger.LogWarning(
                "Could not upload a demo document of {Manifest}: {Code}",
                manifestPath,
                result.Error.Code
            );
            return false;
        }
        logger.LogInformation(
            "Uploaded the demo document {DocumentId} to establishment {EstablishmentId}",
            result.Value.Id,
            establishmentId
        );
        return true;
    }

    private async Task SetExpiryAsync(
        DemoEstablishment target,
        Guid establishmentId,
        Document document,
        ManifestDocument entry,
        CancellationToken cancellationToken
    )
    {
        await using var scope = TenantScope.Open(scopes, logger, target.TenantId, target.OwnerSub);
        var result = await scope
            .Services.GetRequiredService<IDispatcher>()
            .SendAsync(
                new SetDocumentExpiryCommand(establishmentId, document.Id, entry.ExpiresOn),
                cancellationToken
            );
        if (!result.IsSuccess)
        {
            logger.LogWarning(
                "Could not set the expiry date of the demo document {DocumentId}: {Code}",
                document.Id,
                result.Error.Code
            );
        }
    }

    /// <summary>The establishment's id and its documents with their versions, under its tenant.</summary>
    private async Task<(Guid EstablishmentId, IReadOnlyList<Document> Documents)> ReadAsync(
        DemoEstablishment target,
        CancellationToken cancellationToken
    )
    {
        await using var scope = TenantScope.Open(scopes, logger, target.TenantId, target.OwnerSub);
        var db = scope.Services.GetRequiredService<CoreDbContext>();
        // Read only: disposed without a commit.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var establishmentId = await db
            .Establishments.Where(establishment => establishment.Name == target.Name)
            .Select(establishment => (Guid?)establishment.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidDataException(
                $"The demo establishment '{target.Name}' does not exist yet."
            );
        var documents = await scope
            .Services.GetRequiredService<IDocumentRepository>()
            .ListAsync(establishmentId, cancellationToken);
        return (establishmentId, documents);
    }

    /// <summary>A demo establishment by name, in <paramref name="TenantId"/>, whose documents <paramref name="OwnerSub"/> uploads.</summary>
    internal sealed record DemoEstablishment(Guid TenantId, string Name, string OwnerSub);

    private sealed record Manifest(string Establishment, ManifestDocument[] Documents);

    /// <summary><paramref name="File"/> is relative to the manifest's folder.</summary>
    private sealed record ManifestDocument(
        string Title,
        DocumentType Type,
        string File,
        DateOnly? ExpiresOn = null
    );
}
