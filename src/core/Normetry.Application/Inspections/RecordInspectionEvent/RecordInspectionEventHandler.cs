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
        var refusal = await RefuseCompletionCitingForeignDocumentsAsync(
            request,
            inspection,
            now,
            cancellationToken
        );
        if (refusal is not null)
        {
            return refusal;
        }
        var changed = ApplyEventToInspection(request, inspection, now);
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
    /// Fails the inspection instead of completing it when the completion cites a document that is
    /// not one of the establishment's. Null when the event is no such completion.
    /// </summary>
    private async Task<Result<InspectionChange?>?> RefuseCompletionCitingForeignDocumentsAsync(
        RecordInspectionEventCommand request,
        Inspection inspection,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var @event = request.Event;
        if (
            @event is not InspectionCompleted completion
            || await ForeignDocumentsAsync(completion, cancellationToken) is not [_, ..] foreign
        )
        {
            return null;
        }
        // The AI service searches one establishment's chunks, so this is a bug or an attack, and
        // a redelivery would cite the same: fail the inspection now (the inbox keeps it from
        // coming back) instead of dead-lettering it into a wait for the timeout.
        logger.LogWarning(
            "{MessageType} {MessageId} of inspection {InspectionId} cites documents {DocumentIds} that are not of establishment {EstablishmentId}; the completion is refused",
            request.MessageType,
            request.MessageId,
            @event.InspectionId,
            foreign,
            @event.EstablishmentId
        );
        return inspection.Fail(InspectionFailureReasons.Error, ForeignDocumentsReasonText, now)
            ? InspectionChange.From(inspection)
            : Unchanged;
    }

    /// <summary>
    /// The cited document ids that are not the establishment's (deleted ones are). RLS hides another
    /// tenant's documents, so those count as foreign too. Only ids are read.
    /// </summary>
    private async Task<Guid[]> ForeignDocumentsAsync(
        InspectionCompleted completion,
        CancellationToken cancellationToken
    )
    {
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
