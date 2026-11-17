using Microsoft.Extensions.Logging;
using Normetry.Application.Abstractions;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Inspections.RecordInspectionEvent;

internal sealed class RecordInspectionEventHandler(
    IInbox inbox,
    IInspectionRepository inspections,
    TimeProvider clock,
    ILogger<RecordInspectionEventHandler> logger
) : ICommandHandler<RecordInspectionEventCommand, InspectionChange?>
{
    private static readonly Result<InspectionChange?> Unchanged = Result<InspectionChange?>.Success(
        null
    );

    public async Task<Result<InspectionChange?>> HandleAsync(
        RecordInspectionEventCommand request,
        CancellationToken cancellationToken
    )
    {
        if (!await inbox.TryAcceptAsync(request.MessageId, request.MessageType, cancellationToken))
        {
            return Unchanged;
        }

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
            return Unchanged;
        }

        var now = clock.GetUtcNow();
        var changed = @event switch
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
                @event.GetType().Name,
                null
            ),
        };
        return changed ? InspectionChange.From(inspection) : Unchanged;
    }
}
