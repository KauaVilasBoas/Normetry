using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Normetry.Infrastructure.Persistence;

namespace Normetry.Infrastructure.Messaging;

/// <summary>
/// Publishes pending outbox rows every second. Each tenant of <see cref="OutboxTenantList"/> gets its
/// own scope and transaction (RLS on the tenant of its rows): the batch is locked with
/// <c>FOR UPDATE SKIP LOCKED</c>, so two API instances never publish the same row at once, and a
/// row is marked sent only after the broker confirmed it. A crash between the confirm and the
/// commit publishes the row again: delivery is at least once and consumers deduplicate.
/// </summary>
internal sealed class OutboxDispatcher(
    IServiceScopeFactory scopes,
    RabbitMqPublisher publisher,
    IConfiguration configuration,
    TimeProvider clock,
    ILogger<OutboxDispatcher> logger
) : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly string tenantListConnectionString = CoreDatabase.GetDispatcherConnectionString(
        configuration
    );

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = PollInterval;
            try
            {
                await DispatchAllAsync(stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(error, "Outbox dispatch failed; retrying");
                delay = RetryDelay;
            }
            await Task.Delay(delay, stoppingToken)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    public async Task DispatchAllAsync(CancellationToken cancellationToken)
    {
        // ponytail: every listed tenant is visited on each pass (one short transaction each);
        // switch to a wake-up signal or a pending flag when there are many tenants.
        foreach (
            var tenantId in await OutboxTenantList.ListAsync(
                tenantListConnectionString,
                cancellationToken
            )
        )
        {
            // One tenant's failure must not hold back the tenants after it.
            try
            {
                await DispatchAsync(tenantId, cancellationToken);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(error, "Outbox dispatch failed for tenant {TenantId}", tenantId);
            }
        }
    }

    /// <summary>Publishes one batch of the tenant's pending rows and returns how many were sent.</summary>
    public async Task<int> DispatchAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BackgroundTenant>().Set(tenantId);
        using var log = logger.BeginScope(
            new Dictionary<string, object> { ["TenantId"] = tenantId }
        );
        var db = scope.ServiceProvider.GetRequiredService<CoreDbContext>();
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
                await publisher.PublishAsync(message, cancellationToken);
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
}
