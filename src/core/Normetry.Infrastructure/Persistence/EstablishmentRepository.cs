using Microsoft.EntityFrameworkCore;
using Normetry.Application.Establishments;
using Normetry.Domain.Establishments;

namespace Normetry.Infrastructure.Persistence;

// No tenant filter here: row-level security returns only the rows of the transaction's tenant.
internal sealed class EstablishmentRepository(CoreDbContext db) : IEstablishmentRepository
{
    public void Add(Establishment establishment) => db.Establishments.Add(establishment);

    public Task<Establishment?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db
            .Establishments.AsNoTracking()
            .FirstOrDefaultAsync(establishment => establishment.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Establishment>> ListAsync(
        CancellationToken cancellationToken
    ) =>
        await db
            .Establishments.AsNoTracking()
            .OrderBy(establishment => establishment.Name)
            .ThenBy(establishment => establishment.Id)
            .ToListAsync(cancellationToken);
}
