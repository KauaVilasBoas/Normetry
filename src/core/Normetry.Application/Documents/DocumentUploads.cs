using Normetry.Application.Abstractions;
using Normetry.Application.Identity;
using Normetry.Domain.Documents;

namespace Normetry.Application.Documents;

/// <summary>What the upload of a first or a new version shares.</summary>
internal static class DocumentUploads
{
    /// <summary>The caller's user id. They hold a permission, so their user row exists.</summary>
    public static async Task<Guid> UploaderAsync(
        IUserRepository users,
        ICurrentUser currentUser,
        CancellationToken cancellationToken
    ) =>
        await users.FindIdAsync(currentUser.UserId, cancellationToken)
        ?? throw new InvalidOperationException("The caller has no user row.");

    /// <summary>
    /// Stores the file, then announces the version: the blob exists before the message can, and
    /// the message is saved only if the transaction that saves the version commits.
    /// </summary>
    public static async Task StoreAsync(
        IDocumentBlobStore blobs,
        IOutbox outbox,
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

    /// <summary>The document with every uploader's display name.</summary>
    public static async Task<DocumentResponse> ResponseAsync(
        IUserRepository users,
        Document document,
        CancellationToken cancellationToken
    ) =>
        DocumentResponse.From(
            document,
            await users.GetDisplayNamesAsync(
                document.Versions.Select(version => version.UploadedBy).Distinct(),
                cancellationToken
            )
        );
}
