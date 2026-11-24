using Normetry.Application.Abstractions;
using Normetry.Application.Establishments;
using Normetry.Application.Identity;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Documents;

namespace Normetry.Application.Documents.UploadDocument;

internal sealed class UploadDocumentHandler(
    IEstablishmentRepository establishments,
    IDocumentRepository documents,
    IUserRepository users,
    DocumentUploadService uploads,
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
        var locked = await establishments.LockOrNotFoundAsync(
            request.EstablishmentId,
            cancellationToken
        );
        if (!locked.IsSuccess)
        {
            return locked.Error;
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
            await users.CallerIdAsync(currentUser, cancellationToken),
            clock.GetUtcNow()
        );
        document.SetExpiry(request.ExpiresOn);
        await uploads.StoreAsync(
            document,
            document.Newest,
            request.File!.Content,
            cancellationToken
        );
        documents.Add(document);
        return await DocumentResponse.LoadAsync(users, document, cancellationToken);
    }
}
