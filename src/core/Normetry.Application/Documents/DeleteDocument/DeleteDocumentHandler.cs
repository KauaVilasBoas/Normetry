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
        if (
            await documents.GetForUpdateAsync(request.Id, cancellationToken)
            is not { IsDeleted: false } document
        )
        {
            return DocumentErrors.NotFound;
        }
        document.Delete(clock.GetUtcNow());
        await outbox.EnqueueAsync(
            new DocumentDeleted(document.EstablishmentId, document.Id),
            cancellationToken
        );
        return Result.Success();
    }
}
