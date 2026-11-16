using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Normetry.Application.Documents;
using Normetry.Application.Documents.RecordIngestion;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.Infrastructure.Persistence;

namespace Normetry.Infrastructure.Messaging;

internal enum ConsumeOutcome
{
    Ack,

    /// <summary>Wait, then reject with requeue: the broker counts the delivery.</summary>
    Retry,

    /// <summary>Reject without requeue, which dead-letters the message at once.</summary>
    DeadLetter,
}

/// <summary>
/// Handles one delivery of <c>core.document-status</c>. The tenant of the scope comes from the
/// message envelope and from nothing else (not from AMQP properties or headers); the scope's
/// database is tenant-scoped like a request's, so the message reaches only its tenant's rows.
/// </summary>
internal sealed class DocumentStatusMessages(
    IServiceScopeFactory scopes,
    ILogger<DocumentStatusMessages> logger
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
        if (message.Payload is not IDocumentVersionEvent @event)
        {
            logger.LogWarning(
                "Dead-lettering {MessageType} {MessageId}: this queue takes status events only",
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
                new RecordIngestionCommand(message.MessageId, message.Type, @event),
                cancellationToken
            );
        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"Recording failed: {result.Error.Code}.");
        }
        // The transaction has committed: push the change, to this message's tenant only.
        if (result.Value is { } change)
        {
            await Notify.SafelyAsync(
                scope.Services.GetRequiredService<IDocumentStatusNotifier>(),
                message.TenantId,
                [change],
                logger,
                cancellationToken
            );
        }
        return ConsumeOutcome.Ack;
    }
}

internal static class Notify
{
    /// <summary>
    /// A push that fails is logged, not retried: the change is committed, and the web app refetches
    /// after a reconnect.
    /// </summary>
    public static async Task SafelyAsync(
        IDocumentStatusNotifier notifier,
        Guid tenantId,
        IEnumerable<DocumentStatusChange> changes,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        foreach (var change in changes)
        {
            try
            {
                await notifier.NotifyAsync(tenantId, change, cancellationToken);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(
                    error,
                    "Could not push the status of version {VersionId}",
                    change.VersionId
                );
            }
        }
    }
}
