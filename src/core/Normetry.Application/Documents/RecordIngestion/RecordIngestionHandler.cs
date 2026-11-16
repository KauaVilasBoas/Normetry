using Microsoft.Extensions.Logging;
using Normetry.Application.Abstractions;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Documents.RecordIngestion;

internal sealed class RecordIngestionHandler(
    IInbox inbox,
    IDocumentRepository documents,
    TimeProvider clock,
    ILogger<RecordIngestionHandler> logger
) : ICommandHandler<RecordIngestionCommand, DocumentStatusChange?>
{
    private static readonly Result<DocumentStatusChange?> Unchanged =
        Result<DocumentStatusChange?>.Success(null);

    public async Task<Result<DocumentStatusChange?>> HandleAsync(
        RecordIngestionCommand request,
        CancellationToken cancellationToken
    )
    {
        if (!await inbox.TryAcceptAsync(request.MessageId, request.MessageType, cancellationToken))
        {
            return Unchanged;
        }

        var @event = request.Event;
        // RLS shows only the message's tenant, so another tenant's ids find nothing here.
        var document = await documents.GetForUpdateAsync(@event.DocumentId, cancellationToken);
        var version = document?.Versions.FirstOrDefault(version => version.Id == @event.VersionId);
        if (
            document is null
            || version is null
            || document.EstablishmentId != @event.EstablishmentId
            || version.Number != @event.VersionNumber
        )
        {
            logger.LogWarning(
                "{MessageType} {MessageId} names no version of this tenant; dropped",
                request.MessageType,
                request.MessageId
            );
            return Unchanged;
        }

        var now = clock.GetUtcNow();
        var changed = @event switch
        {
            DocumentIngestionStarted => version.StartProcessing(now),
            DocumentIndexed indexed => version.MarkIndexed(
                indexed.ChunkCount,
                indexed.PageCount,
                indexed.Language,
                now
            ),
            DocumentIngestionFailed failed => version.MarkFailed(
                failed.ReasonCode,
                failed.ReasonText,
                now
            ),
            _ => throw new ArgumentOutOfRangeException(
                nameof(request),
                @event.GetType().Name,
                null
            ),
        };
        return changed ? DocumentStatusChange.From(document, version) : Unchanged;
    }
}
