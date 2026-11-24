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
        var refusal = await LockEstablishmentAndRefuseWhenFullAsync(
            request.EstablishmentId,
            cancellationToken
        );
        if (refusal is not null)
        {
            return refusal;
        }

        var document = await CreateCallersDocumentAsync(request, file.Value, cancellationToken);
        // ponytail: the blob upload runs inside the transaction, holding the establishment lock;
        // move it out (store first, then lock and count) if uploads per establishment queue up.
        await uploads.StoreAsync(
            document,
            document.Newest,
            request.File!.Content,
            cancellationToken
        );
        documents.Add(document);
        return await DocumentResponse.LoadAsync(users, document, cancellationToken);
    }

    /// <summary>
    /// Locks the establishment until the transaction ends, so two uploads cannot both take its
    /// last free place, then refuses when it is not found or already holds the most documents
    /// allowed.
    /// </summary>
    private async Task<Error?> LockEstablishmentAndRefuseWhenFullAsync(
        Guid establishmentId,
        CancellationToken cancellationToken
    )
    {
        var locked = await establishments.LockOrNotFoundAsync(establishmentId, cancellationToken);
        if (!locked.IsSuccess)
        {
            return locked.Error;
        }
        var count = await documents.CountAsync(establishmentId, cancellationToken);
        return count >= options.MaxDocumentsPerEstablishment
            ? DocumentErrors.LimitReached(options.MaxDocumentsPerEstablishment)
            : null;
    }

    /// <summary>The requested document, its first version uploaded by the caller now.</summary>
    private async Task<Document> CreateCallersDocumentAsync(
        UploadDocumentCommand request,
        DocumentFile file,
        CancellationToken cancellationToken
    )
    {
        var document = Document.Create(
            tenant.TenantId,
            request.EstablishmentId,
            request.Title,
            request.Type,
            file,
            await users.CallerIdAsync(currentUser, cancellationToken),
            clock.GetUtcNow()
        );
        document.SetExpiry(request.ExpiresOn);
        return document;
    }
}
