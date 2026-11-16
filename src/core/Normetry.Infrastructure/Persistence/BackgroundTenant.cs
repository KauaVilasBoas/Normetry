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
public sealed class BackgroundTenant() : SetOnce<Guid>("tenant"), ITenantContext
{
    public Guid TenantId => Value;

    internal void Set(Guid tenant)
    {
        if (tenant == Guid.Empty)
        {
            throw new ArgumentException(
                "The tenant id must not be the empty UUID.",
                nameof(tenant)
            );
        }
        SetValue(tenant);
    }
}
