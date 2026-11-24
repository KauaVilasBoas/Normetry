using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Normetry.Application.Abstractions;
using Normetry.Application.Documents;
using Normetry.Application.Documents.TimeOutProcessing;
using Normetry.Application.Inspections;
using Normetry.Application.Inspections.TimeOutInspections;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.Infrastructure.Persistence;

namespace Normetry.Infrastructure.Messaging;

/// <summary>
/// Every 30 seconds, fails with <c>timed_out</c> the versions stuck in Processing past
/// <c>Documents:ProcessingTimeout</c> and the inspections Running, or still Requested, past
/// <c>Inspections:RunningTimeout</c>, in one scope and transaction per tenant and kind (RLS). It
/// runs whether or not the broker is reachable. A late <c>DocumentIndexed</c> still makes such a
/// version Indexed, and a late <c>InspectionCompleted</c> such an inspection Completed.
/// </summary>
internal sealed class TimeoutSweeper(
    IServiceScopeFactory scopes,
    OutboxTenantList tenants,
    ILogger<TimeoutSweeper> logger
) : BackgroundService
{
    private const string WorkName = "Timeout sweep";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await tenants.ForEachAsync(
                    logger,
                    WorkName,
                    async tenantId =>
                    {
                        await SweepDocumentVersionsAsync(tenantId, stoppingToken);
                        await SweepInspectionsAsync(tenantId, stoppingToken);
                    },
                    stoppingToken
                );
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(error, "{Work} failed; retrying", WorkName);
            }
            await Task.Delay(Interval, stoppingToken)
                .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    public Task<IReadOnlyList<DocumentStatusChange>> SweepDocumentVersionsAsync(
        Guid tenantId,
        CancellationToken cancellationToken
    ) =>
        SweepTenantAsync(
            tenantId,
            new TimeOutProcessingCommand(),
            "processing version(s)",
            change => change.VersionId,
            cancellationToken
        );

    public Task<IReadOnlyList<InspectionChange>> SweepInspectionsAsync(
        Guid tenantId,
        CancellationToken cancellationToken
    ) =>
        SweepTenantAsync(
            tenantId,
            new TimeOutInspectionsCommand(),
            "running inspection(s)",
            change => change.InspectionId,
            cancellationToken
        );

    /// <summary>
    /// Runs the time-out command in its own scope and transaction for the tenant, then pushes each
    /// change it committed, to that tenant only.
    /// </summary>
    private async Task<IReadOnlyList<TChange>> SweepTenantAsync<TChange>(
        Guid tenantId,
        ICommand<IReadOnlyList<TChange>> timeOut,
        string timedOutKind,
        Func<TChange, Guid> idOf,
        CancellationToken cancellationToken
    )
    {
        await using var scope = TenantScope.Open(scopes, logger, tenantId);
        var result = await scope
            .Services.GetRequiredService<IDispatcher>()
            .SendAsync(timeOut, cancellationToken);
        if (result.Value.Count > 0)
        {
            logger.LogInformation(
                "Timed out {Count} {TimedOutKind}",
                result.Value.Count,
                timedOutKind
            );
            await Notify.SafelyAsync(
                scope.Services.GetRequiredService<IChangeNotifier<TChange>>(),
                tenantId,
                result.Value,
                idOf,
                logger,
                cancellationToken
            );
        }
        return result.Value;
    }
}
