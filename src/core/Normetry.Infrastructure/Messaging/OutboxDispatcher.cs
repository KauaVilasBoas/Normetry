using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Normetry.Infrastructure.Persistence;
using Npgsql;

namespace Normetry.Infrastructure.Messaging;

/// <summary>
/// Publishes pending outbox rows every second. Each tenant of <c>core.outbox_tenants</c> gets its
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

    private readonly string connectionString = CoreDatabase.GetConnectionString(configuration);

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
        foreach (var tenantId in await ListTenantsAsync(cancellationToken))
        {
            await DispatchAsync(tenantId, cancellationToken);
        }
    }

    /// <summary>Publishes one batch of the tenant's pending rows and returns how many were sent.</summary>
    public async Task<int> DispatchAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BackgroundTenant>().Set(tenantId);
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

    /// <summary>
    /// The one query without a tenant: <c>core.outbox_tenants</c> holds only ids, and its policy
    /// lets <c>app_core</c> read them all. Every row of tenant data is read later, under RLS.
    /// </summary>
    private async Task<List<Guid>> ListTenantsAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT tenant_id FROM core.outbox_tenants",
            connection
        );
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var tenants = new List<Guid>();
        while (await reader.ReadAsync(cancellationToken))
        {
            tenants.Add(reader.GetGuid(0));
        }
        return tenants;
    }
}
