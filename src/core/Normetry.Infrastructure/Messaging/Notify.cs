using Microsoft.Extensions.Logging;
using Normetry.Application.Abstractions;

namespace Normetry.Infrastructure.Messaging;

/// <summary>
/// The push of committed changes to the web clients, shared by the status consumers and the
/// timeout sweeper.
/// </summary>
internal static class Notify
{
    /// <summary>
    /// A push that fails is logged, not retried: the change is committed, and the web app refetches
    /// after a reconnect.
    /// </summary>
    public static async Task SafelyAsync<TChange>(
        IChangeNotifier<TChange> notifier,
        Guid tenantId,
        IEnumerable<TChange> changes,
        Func<TChange, Guid> idOf,
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
                logger.LogWarning(error, "Could not push the change of {ChangeId}", idOf(change));
            }
        }
    }
}
