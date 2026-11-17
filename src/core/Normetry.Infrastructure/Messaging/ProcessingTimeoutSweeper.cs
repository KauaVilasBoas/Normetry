using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Normetry.Application.Documents;
using Normetry.Application.Documents.TimeOutProcessing;
using Normetry.Application.Inspections;
using Normetry.Application.Inspections.TimeOutInspections;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.Infrastructure.Persistence;

namespace Normetry.Infrastructure.Messaging;

/// <summary>
/// Every 30 seconds, fails the versions stuck in Processing past <c>Documents:ProcessingTimeout</c>
/// and the inspections stuck in Running past <c>Inspections:RunningTimeout</c> with
/// <c>timed_out</c>, in one scope and transaction per tenant and kind (RLS). It runs whether or not
/// the broker is reachable. A late <c>DocumentIndexed</c> still makes such a version Indexed, and
/// a late <c>InspectionCompleted</c> such an inspection Completed.
/// </summary>
internal sealed class ProcessingTimeoutSweeper(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    ILogger<ProcessingTimeoutSweeper> logger
) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    private readonly string tenantListConnectionString = CoreDatabase.GetDispatcherConnectionString(
        configuration
    );

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await OutboxTenantList.ForEachAsync(
                    tenantListConnectionString,
                    logger,
                    "Processing timeout sweep",
                    async tenantId =>
                    {
                        await SweepAsync(tenantId, stoppingToken);
                        await SweepInspectionsAsync(tenantId, stoppingToken);
                    },
                    stoppingToken
                );
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
        await using var scope = TenantScope.Open(scopes, logger, tenantId);
        var result = await scope
            .Services.GetRequiredService<IDispatcher>()
            .SendAsync(new TimeOutProcessingCommand(), cancellationToken);
        if (result.Value.Count > 0)
        {
            logger.LogInformation("Timed out {Count} processing version(s)", result.Value.Count);
            var notifier = scope.Services.GetRequiredService<IDocumentStatusNotifier>();
            await Notify.SafelyAsync(
                result.Value,
                (change, token) => notifier.NotifyAsync(tenantId, change, token),
                change => change.VersionId,
                logger,
                cancellationToken
            );
        }
        return result.Value;
    }

    public async Task<IReadOnlyList<InspectionChange>> SweepInspectionsAsync(
        Guid tenantId,
        CancellationToken cancellationToken
    )
    {
        await using var scope = TenantScope.Open(scopes, logger, tenantId);
        var result = await scope
            .Services.GetRequiredService<IDispatcher>()
            .SendAsync(new TimeOutInspectionsCommand(), cancellationToken);
        if (result.Value.Count > 0)
        {
            logger.LogInformation("Timed out {Count} running inspection(s)", result.Value.Count);
            var notifier = scope.Services.GetRequiredService<IInspectionNotifier>();
            await Notify.SafelyAsync(
                result.Value,
                (change, token) => notifier.NotifyAsync(tenantId, change, token),
                change => change.InspectionId,
                logger,
                cancellationToken
            );
        }
        return result.Value;
    }
}
