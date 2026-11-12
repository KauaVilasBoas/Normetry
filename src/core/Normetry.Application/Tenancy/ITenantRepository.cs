using Normetry.Domain.Tenancy;

namespace Normetry.Application.Tenancy;

/// <summary>
/// Tenant rows. Row-level security shows a session only the row of its own tenant, so another
/// tenant's id is never found.
/// </summary>
public interface ITenantRepository
{
    Task<Tenant?> GetAsync(Guid id, CancellationToken cancellationToken);
}
