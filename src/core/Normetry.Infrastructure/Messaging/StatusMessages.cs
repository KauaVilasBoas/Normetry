using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Normetry.Application.Abstractions;
using Normetry.Application.Documents;
using Normetry.Application.Documents.RecordIngestion;
using Normetry.Application.Inspections;
using Normetry.Application.Inspections.RecordInspectionEvent;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.Infrastructure.Persistence;

namespace Normetry.Infrastructure.Messaging;

/// <summary>
/// Handles one delivery of <c>core.document-status</c>: <see cref="RecordIngestionCommand"/>,
/// then <c>DocumentStatusChanged</c>.
/// </summary>
internal sealed class DocumentStatusMessages(
    IServiceScopeFactory scopes,
    ILogger<DocumentStatusMessages> logger
) : StatusMessages<IDocumentVersionEvent, DocumentStatusChange>(scopes, logger)
{
    protected override string EventsThisQueueTakes => "status events";

    protected override ICommand<DocumentStatusChange?> RecordCommand(
        InboundMessage message,
        IDocumentVersionEvent @event
    ) => new RecordIngestionCommand(message.MessageId, message.Type, @event);

    protected override Guid ChangeIdOf(DocumentStatusChange change) => change.VersionId;
}

/// <summary>
/// Handles one delivery of <c>core.inspection-status</c>: <see cref="RecordInspectionEventCommand"/>,
/// which loads the inspection by id under the message's tenant, then <c>InspectionChanged</c>.
/// Evidence is stored as the copies the event carries; nothing here reads the documents.
/// </summary>
internal sealed class InspectionStatusMessages(
    IServiceScopeFactory scopes,
    ILogger<InspectionStatusMessages> logger
) : StatusMessages<IInspectionEvent, InspectionChange>(scopes, logger)
{
    protected override string EventsThisQueueTakes => "inspection events";

    protected override ICommand<InspectionChange?> RecordCommand(
        InboundMessage message,
        IInspectionEvent @event
    ) => new RecordInspectionEventCommand(message.MessageId, message.Type, @event);

    protected override Guid ChangeIdOf(InspectionChange change) => change.InspectionId;
}

/// <summary>
/// Handles one delivery of a core status queue. The tenant of the scope comes from the message
/// envelope and from nothing else (not from AMQP properties or headers); the scope's database is
/// tenant-scoped like a request's, so the message reaches only its tenant's rows. A body off the
/// contract, or an event this queue does not take, is dead-lettered. A recording that fails
/// throws, so the consumer retries the delivery. A change is pushed only after its transaction
/// has committed.
/// </summary>
internal abstract class StatusMessages<TEvent, TChange>(IServiceScopeFactory scopes, ILogger logger)
    where TEvent : class
    where TChange : class
{
    /// <summary>What the dead-letter line says this queue takes, such as "status events".</summary>
    protected abstract string EventsThisQueueTakes { get; }

    /// <summary>The command that applies the event, once per message id.</summary>
    protected abstract ICommand<TChange?> RecordCommand(InboundMessage message, TEvent @event);

    /// <summary>The id a failed push is logged with.</summary>
    protected abstract Guid ChangeIdOf(TChange change);

    public async Task<ConsumeOutcome> HandleAsync(
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken
    )
    {
        if (ReadEventOfThisQueue(body) is not var (message, @event))
        {
            return ConsumeOutcome.DeadLetter;
        }

        Activity.Current?.SetTag(MessagingTelemetry.TenantIdTag, message.TenantId.ToString());
        await using var scope = TenantScope.Open(scopes, logger, message.TenantId);
        var change = await RecordAsync(scope.Services, message, @event, cancellationToken);
        if (change is not null)
        {
            await PushCommittedChangeAsync(
                scope.Services,
                message.TenantId,
                change,
                cancellationToken
            );
        }
        return ConsumeOutcome.Ack;
    }

    /// <summary>
    /// The envelope and its event, or null, after logging why, for a body off the contract or an
    /// event this queue does not take.
    /// </summary>
    private (InboundMessage Message, TEvent Event)? ReadEventOfThisQueue(ReadOnlyMemory<byte> body)
    {
        InboundMessage message;
        try
        {
            message = MessageJson.Parse(body.Span);
        }
        catch (MessageFormatException error)
        {
            logger.LogWarning("Dead-lettering a message off the contract: {Reason}", error.Message);
            return null;
        }
        if (message.Payload is not TEvent @event)
        {
            logger.LogWarning(
                "Dead-lettering {MessageType} {MessageId}: this queue takes {EventsThisQueueTakes} only",
                message.Type,
                message.MessageId,
                EventsThisQueueTakes
            );
            return null;
        }
        return (message, @event);
    }

    /// <summary>
    /// Dispatches the record command in the scope (one transaction) and returns its change, or
    /// null when it changed nothing. A failed result throws, so the consumer retries.
    /// </summary>
    private async Task<TChange?> RecordAsync(
        IServiceProvider services,
        InboundMessage message,
        TEvent @event,
        CancellationToken cancellationToken
    )
    {
        var result = await services
            .GetRequiredService<IDispatcher>()
            .SendAsync(RecordCommand(message, @event), cancellationToken);
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Recording failed: {result.Error.Code}.");
        }
        return result.Value;
    }

    /// <summary>The transaction has committed: push the change, to this message's tenant only.</summary>
    private Task PushCommittedChangeAsync(
        IServiceProvider services,
        Guid tenantId,
        TChange change,
        CancellationToken cancellationToken
    ) =>
        Notify.SafelyAsync(
            services.GetRequiredService<IChangeNotifier<TChange>>(),
            tenantId,
            [change],
            ChangeIdOf,
            logger,
            cancellationToken
        );
}
