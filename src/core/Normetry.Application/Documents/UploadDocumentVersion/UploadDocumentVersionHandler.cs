using Normetry.Application.Abstractions;
using Normetry.Application.Identity;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Documents;

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
        // Locked, so two uploads of one document cannot both pass the checks below.
        var locked = await documents.LockLiveOrNotFoundAsync(request.DocumentId, cancellationToken);
        if (!locked.IsSuccess)
        {
            return locked.Error;
        }
        var document = locked.Value;
        var refusal = RefuseNewVersionWhileInFlightOrAtLimit(document);
        if (refusal is not null)
        {
            return refusal;
        }

        var version = document.AddVersion(
            file.Value,
            await users.CallerIdAsync(currentUser, cancellationToken),
            clock.GetUtcNow()
        );
        await uploads.StoreAsync(document, version, request.File!.Content, cancellationToken);
        return await DocumentResponse.LoadAsync(users, document, cancellationToken);
    }

    /// <summary>
    /// A new version waits for the one in flight to end, and never passes the most versions
    /// allowed.
    /// </summary>
    private Error? RefuseNewVersionWhileInFlightOrAtLimit(Document document)
    {
        if (document.HasVersionInFlight)
        {
            return DocumentErrors.VersionInFlight;
        }
        return document.Versions.Count >= options.MaxVersionsPerDocument
            ? DocumentErrors.VersionLimitReached(options.MaxVersionsPerDocument)
            : null;
    }
}
