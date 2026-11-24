using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Normetry.Application.Documents;

namespace Normetry.Infrastructure.Storage;

/// <summary>
/// Document files in one blob container (<c>ConnectionStrings:blobs</c>,
/// <c>Documents:BlobContainer</c>, default <c>documents</c>): Azurite locally, Azure Blob Storage
/// in production, the same SDK in both. The AppHost creates the container; this creates it too
/// on first use when it is missing.
/// </summary>
internal sealed class BlobDocumentStore(BlobContainerClient container) : IDocumentBlobStore
{
    public const string ConnectionStringName = "blobs";

    public const string ContainerSetting = $"{DocumentsOptions.Section}:BlobContainer";

    /// <summary>The container when <see cref="ContainerSetting"/> is not set.</summary>
    public const string DefaultContainer = "documents";

    private volatile bool containerExists;

    public async Task UploadAsync(
        string path,
        byte[] content,
        string contentType,
        CancellationToken cancellationToken
    )
    {
        if (!containerExists)
        {
            await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            containerExists = true;
        }
        await container
            .GetBlobClient(path)
            .UploadAsync(
                new BinaryData(content),
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
                    // Every version has its own path: an existing blob is never replaced.
                    Conditions = new BlobRequestConditions { IfNoneMatch = ETag.All },
                },
                cancellationToken
            );
    }
}
