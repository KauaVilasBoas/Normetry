using Microsoft.EntityFrameworkCore;
using Normetry.Application.Tenancy;
using Normetry.Domain.Tenancy;

namespace Normetry.Infrastructure.Persistence;

// No tenant filter here: row-level security returns only the transaction's own tenant row.
internal sealed class TenantRepository(CoreDbContext db) : ITenantRepository
{
    public Task<Tenant?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db.Tenants.AsNoTracking().FirstOrDefaultAsync(tenant => tenant.Id == id, cancellationToken);
}
