using Normetry.Application.Abstractions;

namespace Normetry.Infrastructure.Persistence;

/// <summary>
/// The tenant of a scope that a background service opens for one tenant: the outbox dispatcher
/// (from the outbox row), the queue consumer (from the message envelope, its only tenant source)
/// and the processing timeout sweeper. Set once, before anything in the scope resolves
/// <see cref="ITenantContext"/>; the scope's database then goes through
/// <see cref="CoreDatabase.UseTenantScopedCoreDatabase"/> like a request's. An HTTP request never
/// sets it, so its tenant still comes only from the token.
/// </summary>
public sealed class BackgroundTenant : ITenantContext
{
    private Guid? tenantId;
    private bool resolved;

    /// <summary>
    /// Read once, when the scope resolves <see cref="ITenantContext"/>; from then on the scope's
    /// tenant source is fixed, so a late <see cref="Set"/> throws instead of being ignored.
    /// </summary>
    public bool IsSet
    {
        get
        {
            resolved = true;
            return tenantId is not null;
        }
    }

    public Guid TenantId =>
        tenantId ?? throw new InvalidOperationException("The background scope has no tenant.");

    internal void Set(Guid tenant)
    {
        if (tenant == Guid.Empty)
        {
            throw new ArgumentException(
                "The tenant id must not be the empty UUID.",
                nameof(tenant)
            );
        }
        if (tenantId is not null || resolved)
        {
            throw new InvalidOperationException(
                "The background scope already has a tenant, or resolved one before it was set."
            );
        }
        tenantId = tenant;
    }
}
