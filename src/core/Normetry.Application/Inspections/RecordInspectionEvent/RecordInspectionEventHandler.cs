using Microsoft.Extensions.Logging;
using Normetry.Application.Abstractions;
using Normetry.Application.Documents;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Inspections;

namespace Normetry.Application.Inspections.RecordInspectionEvent;

internal sealed class RecordInspectionEventHandler(
    IInbox inbox,
    IInspectionRepository inspections,
    IDocumentRepository documents,
    TimeProvider clock,
    ILogger<RecordInspectionEventHandler> logger
) : InboxCommandHandler<RecordInspectionEventCommand, InspectionChange>(inbox)
{
    private const string ForeignDocumentsReasonText =
        "The inspection cited a document that is not one of this establishment's.";

    protected override async Task<Result<InspectionChange?>> ApplyAsync(
        RecordInspectionEventCommand request,
        CancellationToken cancellationToken
    )
    {
        var inspection = await LockInspectionOrLogDroppedEventAsync(request, cancellationToken);
        if (inspection is null)
        {
            return Unchanged;
        }
        var now = clock.GetUtcNow();
        var foreignDocuments = await ForeignDocumentsCitedAsync(request.Event, cancellationToken);
        var changed = foreignDocuments is [_, ..]
            ? FailForForeignDocuments(request, inspection, foreignDocuments, now)
            : ApplyEventToInspection(request, inspection, now);
        return changed ? InspectionChange.From(inspection) : Unchanged;
    }

    /// <summary>
    /// The inspection the event names, locked, or null (logged) when this tenant has no such
    /// inspection of the event's establishment.
    /// </summary>
    private async Task<Inspection?> LockInspectionOrLogDroppedEventAsync(
        RecordInspectionEventCommand request,
        CancellationToken cancellationToken
    )
    {
        var @event = request.Event;
        // RLS shows only the message's tenant, so another tenant's ids find nothing here.
        var inspection = await inspections.GetForUpdateAsync(
            @event.InspectionId,
            cancellationToken
        );
        if (inspection is null || inspection.EstablishmentId != @event.EstablishmentId)
        {
            logger.LogWarning(
                "{MessageType} {MessageId} names no inspection {InspectionId} of establishment {EstablishmentId} here; dropped",
                request.MessageType,
                request.MessageId,
                @event.InspectionId,
                @event.EstablishmentId
            );
            return null;
        }
        return inspection;
    }

    /// <summary>
    /// The document ids a completion cites that are not the establishment's (deleted ones are);
    /// none for any other event. RLS hides another tenant's documents, so those count as foreign
    /// too. Only ids are read.
    /// </summary>
    private async Task<Guid[]> ForeignDocumentsCitedAsync(
        IInspectionEvent @event,
        CancellationToken cancellationToken
    )
    {
        if (@event is not InspectionCompleted completion)
        {
            return [];
        }
        var cited = completion
            .Findings.SelectMany(finding => finding.EvidenceCitations)
            .Select(citation => citation.DocumentId)
            .ToHashSet();
        if (cited.Count == 0)
        {
            return [];
        }
        cited.ExceptWith(
            await documents.FindOfEstablishmentAsync(
                completion.EstablishmentId,
                cited,
                cancellationToken
            )
        );
        return [.. cited];
    }

    /// <summary>
    /// Fails the inspection instead of completing it, since the completion cites documents that are
    /// not the establishment's; true when that changed the inspection.
    /// </summary>
    private bool FailForForeignDocuments(
        RecordInspectionEventCommand request,
        Inspection inspection,
        Guid[] foreignDocuments,
        DateTimeOffset now
    )
    {
        // The AI service searches one establishment's chunks, so this is a bug or an attack, and
        // a redelivery would cite the same: fail the inspection now (the inbox keeps it from
        // coming back) instead of dead-lettering it into a wait for the timeout.
        logger.LogWarning(
            "{MessageType} {MessageId} of inspection {InspectionId} cites documents {DocumentIds} that are not of establishment {EstablishmentId}; the completion is refused",
            request.MessageType,
            request.MessageId,
            request.Event.InspectionId,
            foreignDocuments,
            request.Event.EstablishmentId
        );
        return inspection.Fail(InspectionFailureReasons.Error, ForeignDocumentsReasonText, now);
    }

    /// <summary>Runs the event's domain transition; true when it changed the inspection.</summary>
    private static bool ApplyEventToInspection(
        RecordInspectionEventCommand request,
        Inspection inspection,
        DateTimeOffset now
    ) =>
        request.Event switch
        {
            InspectionStarted started => inspection.Start(started.ItemsTotal, started.Themes, now),
            InspectionProgressed progressed => inspection.Progress(
                progressed.ItemsEvaluated,
                progressed.ItemsTotal,
                progressed.Themes,
                now
            ),
            InspectionCompleted completed => inspection.Complete(
                completed.InputTokens,
                completed.OutputTokens,
                completed.CostUsd,
                completed.Findings,
                now
            ),
            InspectionFailed failed => inspection.Fail(failed.ReasonCode, failed.ReasonText, now),
            _ => throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Event.GetType().Name,
                null
            ),
        };
}
