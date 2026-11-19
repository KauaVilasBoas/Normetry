using Microsoft.Extensions.Logging;
using Normetry.Application.Abstractions;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Documents;

namespace Normetry.Application.Documents.RecordIngestion;

internal sealed class RecordIngestionHandler(
    IInbox inbox,
    IDocumentRepository documents,
    TimeProvider clock,
    ILogger<RecordIngestionHandler> logger
) : InboxCommandHandler<RecordIngestionCommand, DocumentStatusChange>(inbox)
{
    protected override async Task<Result<DocumentStatusChange?>> ApplyAsync(
        RecordIngestionCommand request,
        CancellationToken cancellationToken
    )
    {
        var located = await LockVersionOrLogDroppedEventAsync(request, cancellationToken);
        if (located is null)
        {
            return Unchanged;
        }
        var (document, version) = located.Value;
        var changed = ApplyEventToVersion(request, version, clock.GetUtcNow());
        return changed ? DocumentStatusChange.From(document, version) : Unchanged;
    }

    /// <summary>
    /// The document, locked, and the version the event names, or null (logged) when this tenant
    /// has no such version of the event's establishment.
    /// </summary>
    private async Task<(
        Document Document,
        DocumentVersion Version
    )?> LockVersionOrLogDroppedEventAsync(
        RecordIngestionCommand request,
        CancellationToken cancellationToken
    )
    {
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
            return null;
        }
        return (document, version);
    }

    /// <summary>Runs the event's domain transition; true when it changed the version.</summary>
    private static bool ApplyEventToVersion(
        RecordIngestionCommand request,
        DocumentVersion version,
        DateTimeOffset now
    ) =>
        request.Event switch
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
                request.Event.GetType().Name,
                null
            ),
        };
}
