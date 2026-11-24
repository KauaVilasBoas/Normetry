using Normetry.Application.Abstractions;
using Normetry.Application.Identity;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Documents.UploadDocumentVersion;

internal sealed class UploadDocumentVersionHandler(
    IDocumentRepository documents,
    IUserRepository users,
    DocumentUploadService uploads,
    ICurrentUser currentUser,
    DocumentsOptions options,
    TimeProvider clock
) : ICommandHandler<UploadDocumentVersionCommand, DocumentResponse>
{
    public async Task<Result<DocumentResponse>> HandleAsync(
        UploadDocumentVersionCommand request,
        CancellationToken cancellationToken
    )
    {
        var file = DocumentFiles.Inspect(request.File!);
        if (!file.IsSuccess)
        {
            return file.Error;
        }
        // Locked, so two uploads of one document cannot both pass the check below.
        var locked = await documents.LockLiveOrNotFoundAsync(request.DocumentId, cancellationToken);
        if (!locked.IsSuccess)
        {
            return locked.Error;
        }
        var document = locked.Value;
        if (document.HasVersionInFlight)
        {
            return DocumentErrors.VersionInFlight;
        }
        if (document.Versions.Count >= options.MaxVersionsPerDocument)
        {
            return DocumentErrors.VersionLimitReached(options.MaxVersionsPerDocument);
        }

        var version = document.AddVersion(
            file.Value,
            await users.CallerIdAsync(currentUser, cancellationToken),
            clock.GetUtcNow()
        );
        await uploads.StoreAsync(document, version, request.File!.Content, cancellationToken);
        return await DocumentResponse.LoadAsync(users, document, cancellationToken);
    }
}
