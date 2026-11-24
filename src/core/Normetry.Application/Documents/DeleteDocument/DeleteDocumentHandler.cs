using Normetry.Application.Abstractions;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Documents.DeleteDocument;

internal sealed class DeleteDocumentHandler(
    IDocumentRepository documents,
    IOutbox outbox,
    TimeProvider clock
) : ICommandHandler<DeleteDocumentCommand>
{
    public async Task<Result> HandleAsync(
        DeleteDocumentCommand request,
        CancellationToken cancellationToken
    )
    {
        var locked = await documents.LockLiveOrNotFoundAsync(request.Id, cancellationToken);
        if (!locked.IsSuccess)
        {
            return locked.Error;
        }
        var document = locked.Value;
        document.Delete(clock.GetUtcNow());
        await outbox.EnqueueAsync(
            new DocumentDeleted(document.EstablishmentId, document.Id),
            cancellationToken
        );
        return Result.Success();
    }
}
