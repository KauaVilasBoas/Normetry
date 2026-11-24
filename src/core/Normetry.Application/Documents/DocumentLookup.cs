using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Documents;

namespace Normetry.Application.Documents;

/// <summary>
/// The one place a handler locks the document its route names, or refuses with
/// <see cref="DocumentErrors.NotFound"/> when it is missing or deleted. Another tenant's document
/// is invisible under row-level security, so it is not found either.
/// </summary>
internal static class DocumentLookup
{
    /// <summary>
    /// The document, tracked with its versions and locked until the transaction ends
    /// (<see cref="IDocumentRepository.GetForUpdateAsync(Guid, CancellationToken)"/>), unless it is
    /// missing or deleted.
    /// </summary>
    public static async Task<Result<Document>> LockLiveOrNotFoundAsync(
        this IDocumentRepository documents,
        Guid documentId,
        CancellationToken cancellationToken
    ) => LiveOrNotFound(await documents.GetForUpdateAsync(documentId, cancellationToken));

    /// <summary>
    /// As <see cref="LockLiveOrNotFoundAsync(IDocumentRepository, Guid, CancellationToken)"/>, for
    /// a document of the establishment: another establishment's is not found either.
    /// </summary>
    public static async Task<Result<Document>> LockLiveOrNotFoundAsync(
        this IDocumentRepository documents,
        Guid establishmentId,
        Guid documentId,
        CancellationToken cancellationToken
    ) =>
        LiveOrNotFound(
            await documents.GetForUpdateAsync(establishmentId, documentId, cancellationToken)
        );

    private static Result<Document> LiveOrNotFound(Document? document) =>
        document is { IsDeleted: false } live ? live : DocumentErrors.NotFound;
}
