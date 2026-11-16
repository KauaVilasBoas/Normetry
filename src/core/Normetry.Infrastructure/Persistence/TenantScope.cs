using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Normetry.Infrastructure.Persistence;

/// <summary>
/// A service scope a background service opens for one tenant: <see cref="BackgroundTenant"/> is
/// set before anything in it resolves the tenant, and log lines carry the <c>TenantId</c>.
/// </summary>
internal sealed class TenantScope : IAsyncDisposable
{
    private readonly AsyncServiceScope scope;
    private readonly IDisposable? log;

    private TenantScope(AsyncServiceScope scope, IDisposable? log)
    {
        this.scope = scope;
        this.log = log;
    }

    public IServiceProvider Services => scope.ServiceProvider;

    public static TenantScope Open(IServiceScopeFactory scopes, ILogger logger, Guid tenantId)
    {
        var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<BackgroundTenant>().Set(tenantId);
        return new(
            scope,
            logger.BeginScope(new Dictionary<string, object> { ["TenantId"] = tenantId })
        );
    }

    public async ValueTask DisposeAsync()
    {
        log?.Dispose();
        await scope.DisposeAsync();
    }
}
