using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Normetry.Application.Inspections;
using Normetry.Application.Inspections.RecordInspectionEvent;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.Infrastructure.Persistence;

namespace Normetry.Infrastructure.Messaging;

/// <summary>
/// Handles one delivery of <c>core.inspection-status</c>, like <see cref="DocumentStatusMessages"/>:
/// the scope's tenant comes from the message envelope and from nothing else, and the inspection
/// is loaded by id under that tenant's transaction. Evidence is stored as the copies the event
/// carries; nothing here reads the documents.
/// </summary>
internal sealed class InspectionStatusMessages(
    IServiceScopeFactory scopes,
    ILogger<InspectionStatusMessages> logger
)
{
    public async Task<ConsumeOutcome> HandleAsync(
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken
    )
    {
        InboundMessage message;
        try
        {
            message = MessageJson.Parse(body.Span);
        }
        catch (MessageFormatException error)
        {
            logger.LogWarning("Dead-lettering a message off the contract: {Reason}", error.Message);
            return ConsumeOutcome.DeadLetter;
        }
        if (message.Payload is not IInspectionEvent @event)
        {
            logger.LogWarning(
                "Dead-lettering {MessageType} {MessageId}: this queue takes inspection events only",
                message.Type,
                message.MessageId
            );
            return ConsumeOutcome.DeadLetter;
        }

        Activity.Current?.SetTag("normetry.tenant_id", message.TenantId.ToString());
        await using var scope = TenantScope.Open(scopes, logger, message.TenantId);
        var result = await scope
            .Services.GetRequiredService<IDispatcher>()
            .SendAsync(
                new RecordInspectionEventCommand(message.MessageId, message.Type, @event),
                cancellationToken
            );
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Recording failed: {result.Error.Code}.");
        }
        // The transaction has committed: push the change, to this message's tenant only.
        if (result.Value is { } change)
        {
            var notifier = scope.Services.GetRequiredService<IInspectionNotifier>();
            await Notify.SafelyAsync(
                [change],
                (change, token) => notifier.NotifyAsync(message.TenantId, change, token),
                change => change.InspectionId,
                logger,
                cancellationToken
            );
        }
        return ConsumeOutcome.Ack;
    }
}
