using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Documents.SetDocumentExpiry;

internal sealed class SetDocumentExpiryHandler(IDocumentRepository documents)
    : ICommandHandler<SetDocumentExpiryCommand>
{
    public async Task<Result> HandleAsync(
        SetDocumentExpiryCommand request,
        CancellationToken cancellationToken
    )
    {
        if (
            await documents.GetForUpdateAsync(request.DocumentId, cancellationToken)
                is not { IsDeleted: false } document
            || document.EstablishmentId != request.EstablishmentId
        )
        {
            return DocumentErrors.NotFound;
        }
        document.SetExpiry(request.ExpiresOn);
        return Result.Success();
    }
}
