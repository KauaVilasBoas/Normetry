using Normetry.Application.Abstractions;
using Normetry.Application.Establishments;
using Normetry.Application.Establishments.GetEstablishment;
using Normetry.Application.Identity;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Documents;

namespace Normetry.Application.Documents.UploadDocument;

internal sealed class UploadDocumentHandler(
    IEstablishmentRepository establishments,
    IDocumentRepository documents,
    IUserRepository users,
    IDocumentBlobStore blobs,
    IOutbox outbox,
    ICurrentUser currentUser,
    DocumentsOptions options,
    ITenantContext tenant,
    TimeProvider clock
) : ICommandHandler<UploadDocumentCommand, DocumentResponse>
{
    public async Task<Result<DocumentResponse>> HandleAsync(
        UploadDocumentCommand request,
        CancellationToken cancellationToken
    )
    {
        var file = DocumentFiles.Inspect(request.File!);
        if (!file.IsSuccess)
        {
            return file.Error;
        }
        // Held until the transaction ends, so two uploads cannot both take the last free place.
        if (!await establishments.LockAsync(request.EstablishmentId, cancellationToken))
        {
            return GetEstablishmentHandler.NotFound;
        }
        if (
            await documents.CountAsync(request.EstablishmentId, cancellationToken)
            >= options.MaxDocumentsPerEstablishment
        )
        {
            return DocumentErrors.LimitReached(options.MaxDocumentsPerEstablishment);
        }

        // ponytail: the blob upload runs inside the transaction, holding the establishment lock;
        // move it out (store first, then lock and count) if uploads per establishment queue up.
        var document = Document.Create(
            tenant.TenantId,
            request.EstablishmentId,
            request.Title,
            request.Type,
            file.Value,
            await DocumentUploads.UploaderAsync(users, currentUser, cancellationToken),
            clock.GetUtcNow()
        );
        await DocumentUploads.StoreAsync(
            blobs,
            outbox,
            document,
            document.Newest,
            request.File!.Content,
            cancellationToken
        );
        documents.Add(document);
        return await DocumentUploads.ResponseAsync(users, document, cancellationToken);
    }
}
