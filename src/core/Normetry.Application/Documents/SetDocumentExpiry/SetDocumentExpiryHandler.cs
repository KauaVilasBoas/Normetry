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
        var locked = await documents.LockLiveOrNotFoundAsync(
            request.EstablishmentId,
            request.DocumentId,
            cancellationToken
        );
        if (!locked.IsSuccess)
        {
            return locked.Error;
        }
        locked.Value.SetExpiry(request.ExpiresOn);
        return Result.Success();
    }
}
