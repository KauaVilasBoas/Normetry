using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Normetry.Application.Inspections;
using Normetry.Infrastructure.Persistence;

namespace Normetry.Infrastructure.Messaging;

/// <summary>
/// Publishes pending outbox rows. A tenant whose messages this process just committed is
/// published at once (<see cref="OutboxSignal"/>); every 30 seconds, and after a failure, a full
/// pass visits each tenant of <see cref="OutboxTenantList"/>, for rows a signal did not cover
/// (another API instance, a crash, a broker that was down). Each tenant gets its own scope and
/// transaction (RLS on the tenant of its rows): the batch is locked with
/// <c>FOR UPDATE SKIP LOCKED</c>, so two API instances never publish the same row at once, and a
/// row is marked sent only after the broker confirmed it. A crash between the confirm and the
/// commit publishes the row again: delivery is at least once and consumers deduplicate.
/// <c>InspectionRequested</c> expires when its inspection would time out (<see cref="ExpiresIn"/>).
/// </summary>
internal sealed class OutboxDispatcher(
    IServiceScopeFactory scopes,
    RabbitMqPublisher publisher,
    OutboxSignal signal,
    OutboxTenantList tenants,
    InspectionsOptions inspections,
    TimeProvider clock,
    ILogger<OutboxDispatcher> logger
) : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan FullPassInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchAllAsync(stoppingToken);
                await DispatchSignaledAsync(stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(error, "Outbox dispatch failed; retrying");
                await Task.Delay(RetryDelay, stoppingToken)
                    .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    /// <summary>Until the next full pass is due, publishes the tenants signaled as they come.</summary>
    private async Task DispatchSignaledAsync(CancellationToken stoppingToken)
    {
        using var pass = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        pass.CancelAfter(FullPassInterval);
        try
        {
            while (await signal.Reader.WaitToReadAsync(pass.Token))
            {
                var tenants = new HashSet<Guid>();
                while (signal.Reader.TryRead(out var tenantId))
                {
                    tenants.Add(tenantId);
                }
                foreach (var tenantId in tenants)
                {
                    try
                    {
                        await DrainAsync(tenantId, stoppingToken);
                    }
                    catch (Exception error) when (!stoppingToken.IsCancellationRequested)
                    {
                        // Its rows stay pending for the next full pass.
                        logger.LogWarning(
                            error,
                            "Outbox dispatch failed for tenant {TenantId}",
                            tenantId
                        );
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            // The full pass is due.
        }
    }

    /// <summary>Publishes the tenant's batches until one is not full (all sent, or a failure).</summary>
    private async Task DrainAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        while (await DispatchAsync(tenantId, cancellationToken) == BatchSize) { }
    }

    public async Task DispatchAllAsync(CancellationToken cancellationToken)
    {
        await tenants.ForEachAsync(
            logger,
            "Outbox dispatch",
            tenantId => DrainAsync(tenantId, cancellationToken),
            cancellationToken
        );
    }

    /// <summary>Publishes one batch of the tenant's pending rows and returns how many were sent.</summary>
    public async Task<int> DispatchAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var scope = TenantScope.Open(scopes, logger, tenantId);
        var db = scope.Services.GetRequiredService<CoreDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var pending = await db
            .OutboxMessages.FromSql(
                $"""
                SELECT * FROM core.outbox_messages
                WHERE sent_at IS NULL
                ORDER BY created_at, id
                LIMIT {BatchSize}
                FOR UPDATE SKIP LOCKED
                """
            )
            .ToListAsync(cancellationToken);

        var sent = 0;
        foreach (var message in pending)
        {
            try
            {
                await publisher.PublishAsync(message, ExpiresIn(message), cancellationToken);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                // Unroutable (no queue bound yet), nacked or no broker: the row stays pending and
                // the rest of the batch waits, so a tenant's messages keep their order.
                logger.LogWarning(
                    error,
                    "Could not publish outbox message {MessageId} ({MessageType}); it stays pending",
                    message.Id,
                    message.Type
                );
                break;
            }
            message.SentAt = clock.GetUtcNow();
            sent++;
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return sent;
    }

    /// <summary>
    /// How long the broker keeps the message queued; null for ever. An <c>InspectionRequested</c>
    /// lives until the sweeper would fail its inspection, still Requested, as <c>timed_out</c>:
    /// <see cref="InspectionsOptions.RunningTimeout"/> after the request, which wrote the row. A
    /// worker then never starts an inspection that already failed; the broker dead-letters the
    /// request instead. The time counts only while the message waits in the queue, not while a
    /// worker holds it.
    /// </summary>
    private TimeSpan? ExpiresIn(OutboxMessage message) =>
        message.Type == InspectionRequested.Type
            ? message.CreatedAt + inspections.RunningTimeout - clock.GetUtcNow()
            : null;
}
