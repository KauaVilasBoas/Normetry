using Normetry.Application.Abstractions;
using Normetry.Domain.Documents;

namespace Normetry.Application.Documents;

/// <summary>
/// What the upload of a first or a new version shares. Public only so the composition root can
/// register it; the upload handlers are its only callers.
/// </summary>
public sealed class DocumentUploadService(IDocumentBlobStore blobs, IOutbox outbox)
{
    /// <summary>
    /// Stores the file, then announces the version: the blob exists before the message can, and
    /// the message is saved only if the transaction that saves the version commits.
    /// </summary>
    internal async Task StoreAsync(
        Document document,
        DocumentVersion version,
        byte[] content,
        CancellationToken cancellationToken
    )
    {
        await blobs.UploadAsync(version.BlobPath, content, version.ContentType, cancellationToken);
        await outbox.EnqueueAsync(
            new DocumentUploaded(
                document.EstablishmentId,
                document.Id,
                version.Id,
                version.Number,
                version.BlobPath,
                version.ContentType,
                version.Size,
                version.Sha256,
                document.Title,
                document.Type
            ),
            cancellationToken
        );
    }
}
