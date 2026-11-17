using Microsoft.EntityFrameworkCore;
using Normetry.Application.Identity;
using Normetry.Domain.Identity;

namespace Normetry.Infrastructure.Persistence;

// No tenant filter here: row-level security returns and accepts only the transaction's tenant.
internal sealed class UserRepository(CoreDbContext db) : IUserRepository
{
    public Task SyncAsync(
        Guid tenantId,
        string sub,
        string? displayName,
        string? email,
        CancellationToken cancellationToken
    ) =>
        // One statement, safe under concurrent first requests. Without the tenant row (RLS shows
        // only the transaction's own) the foreign key would fail, so nothing is inserted. An
        // existing row is written only when the token's name or email changed.
        db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO core.users AS u (tenant_id, id, sub, display_name, email)
            SELECT {tenantId}, {Guid.CreateVersion7()}, {sub}, {displayName}::text, {email}::text
            WHERE EXISTS (SELECT FROM core.tenants WHERE id = {tenantId})
            ON CONFLICT (tenant_id, sub) DO UPDATE
                SET display_name = excluded.display_name, email = excluded.email
                WHERE (u.display_name, u.email) IS DISTINCT FROM (excluded.display_name, excluded.email)
            """,
            cancellationToken
        );

    public async Task<IReadOnlyList<ProfileSummary>> GetProfilesAsync(
        string sub,
        CancellationToken cancellationToken
    ) =>
        await HeldProfiles(sub)
            .OrderBy(profile => profile.Name)
            .Select(profile => new ProfileSummary(profile.Id, profile.Name, profile.SystemKey))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<string>> GetPermissionsAsync(
        string sub,
        CancellationToken cancellationToken
    )
    {
        var profiles = HeldProfiles(sub);
        // Owner's grants_all covers every catalog row, present and future.
        var names = await db
            .Permissions.Where(permission =>
                profiles.Any(profile =>
                    profile.GrantsAll
                    || profile.GrantedPermissions.Any(granted =>
                        granted.Permission == permission.Name
                    )
                )
            )
            .Select(permission => permission.Name)
            .ToListAsync(cancellationToken);
        return [.. names.Order(StringComparer.Ordinal)];
    }

    public async Task<IReadOnlyList<UserResponse>> ListAsync(
        string callerSub,
        CancellationToken cancellationToken
    ) =>
        await db
            .Users.OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Id)
            .Select(user => new UserResponse(
                user.Id,
                user.DisplayName,
                user.Email,
                user.Sub == callerSub,
                db.Profiles.Where(profile =>
                        user.Profiles.Any(held => held.ProfileId == profile.Id)
                    )
                    .OrderBy(profile => profile.Name)
                    .Select(profile => new ProfileSummary(
                        profile.Id,
                        profile.Name,
                        profile.SystemKey
                    ))
                    .ToList()
            ))
            .ToListAsync(cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(user => user.Id == id, cancellationToken);

    public async Task<Guid?> FindIdAsync(string sub, CancellationToken cancellationToken) =>
        await db
            .Users.Where(user => user.Sub == sub)
            .Select(user => (Guid?)user.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, string?>> GetDisplayNamesAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken
    )
    {
        var wanted = ids.ToArray();
        return await db
            .Users.Where(user => wanted.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.DisplayName, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetSubsAsync(
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken
    )
    {
        var wanted = ids.ToArray();
        return await db
            .Users.Where(user => wanted.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.Sub, cancellationToken);
    }

    public Task AssignAsync(
        Guid tenantId,
        Guid userId,
        Guid profileId,
        CancellationToken cancellationToken
    ) =>
        // Safe under a repeated or concurrent assign. The composite foreign keys reject a user or
        // profile of another tenant even if RLS let the row through.
        db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO core.user_profiles (tenant_id, user_id, profile_id)
            VALUES ({tenantId}, {userId}, {profileId})
            ON CONFLICT DO NOTHING
            """,
            cancellationToken
        );

    public Task UnassignAsync(Guid userId, Guid profileId, CancellationToken cancellationToken) =>
        db
            .UserProfiles.Where(held => held.UserId == userId && held.ProfileId == profileId)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListHoldersAsync(
        Guid profileId,
        CancellationToken cancellationToken
    ) =>
        await db
            .UserProfiles.Where(held => held.ProfileId == profileId)
            .Select(held => held.UserId)
            .ToListAsync(cancellationToken);

    private IQueryable<Profile> HeldProfiles(string sub)
    {
        var held = db
            .UserProfiles.Where(link =>
                db.Users.Any(user => user.Id == link.UserId && user.Sub == sub)
            )
            .Select(link => link.ProfileId);
        return db.Profiles.Where(profile => held.Contains(profile.Id));
    }
}
