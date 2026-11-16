using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Normetry.Application.Documents;
using Normetry.Application.Documents.TimeOutProcessing;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.Infrastructure.Persistence;

namespace Normetry.Infrastructure.Messaging;

/// <summary>
/// Every 30 seconds, fails the versions stuck in Processing past <c>Documents:ProcessingTimeout</c>
/// with <c>timed_out</c>, in one scope and transaction per tenant (RLS). It runs whether or not
/// the broker is reachable. A late <c>DocumentIndexed</c> still makes such a version Indexed.
/// </summary>
internal sealed class ProcessingTimeoutSweeper(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<ProcessingTimeoutSweeper> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly string connectionString = CoreDatabase.GetConnectionString(configuration);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                foreach (
                    var tenantId in await OutboxTenantList.ListAsync(
                        connectionString,
                        stoppingToken
                    )
                )
                {
                    // One tenant's failure must not hold back the tenants after it.
                    try
                    {
                        await SweepAsync(tenantId, stoppingToken);
                    }
                    catch (Exception error) when (!stoppingToken.IsCancellationRequested)
                    {
                        logger.LogWarning(
                            error,
                            "Processing timeout sweep failed for tenant {TenantId}",
                            tenantId
                        );
                    }
                }
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(error, "Processing timeout sweep failed; retrying");
            }
            await Task.Delay(Interval, stoppingToken)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    public async Task<IReadOnlyList<DocumentStatusChange>> SweepAsync(
        Guid tenantId,
        CancellationToken cancellationToken
    )
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BackgroundTenant>().Set(tenantId);
        using var log = logger.BeginScope(
            new Dictionary<string, object> { ["TenantId"] = tenantId }
        );
        var result = await scope
            .ServiceProvider.GetRequiredService<IDispatcher>()
            .SendAsync(new TimeOutProcessingCommand(), cancellationToken);
        if (result.Value.Count > 0)
        {
            logger.LogInformation("Timed out {Count} processing version(s)", result.Value.Count);
            await Notify.SafelyAsync(
                scope.ServiceProvider.GetRequiredService<IDocumentStatusNotifier>(),
                tenantId,
                result.Value,
                logger,
                cancellationToken
            );
        }
        return result.Value;
    }
}
