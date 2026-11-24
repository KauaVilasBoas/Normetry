using Microsoft.EntityFrameworkCore;
using Normetry.Application.Identity;
using Normetry.Domain.Identity;

namespace Normetry.Infrastructure.Persistence;

// No tenant filter here: row-level security returns only the transaction's tenant rows. The
// catalog has no tenant and is the same for everyone.
internal sealed class ProfileRepository(CoreDbContext db) : IProfileRepository
{
    public void Add(Profile profile) => db.Profiles.Add(profile);

    public void Remove(Profile profile) => db.Profiles.Remove(profile);

    public Task<Profile?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        db
            .Profiles.Include(profile => profile.GrantedPermissions)
            .FirstOrDefaultAsync(profile => profile.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ProfileListItem>> ListAsync(
        CancellationToken cancellationToken
    ) =>
        await db
            .Profiles.OrderBy(profile => profile.Name)
            .ThenBy(profile => profile.Id)
            .Select(profile => new ProfileListItem(
                profile.Id,
                profile.Name,
                profile.SystemKey,
                profile.GrantsAll,
                profile.GrantsAll ? db.Permissions.Count() : profile.GrantedPermissions.Count()
            ))
            .ToListAsync(cancellationToken);

    public Task<bool> NameTakenAsync(string name, CancellationToken cancellationToken) =>
        db.Profiles.AnyAsync(profile => profile.Name == name, cancellationToken);

    public Task LockAsync(Guid id, CancellationToken cancellationToken) =>
        db.LockByIdAsync<Profile>(id, cancellationToken);

    public async Task<IReadOnlyList<string>> ListCatalogAsync(
        CancellationToken cancellationToken
    ) =>
        (await db.Permissions.Select(permission => permission.Name).ToListAsync(cancellationToken))
            .Order(StringComparer.Ordinal)
            .ToArray();
}
