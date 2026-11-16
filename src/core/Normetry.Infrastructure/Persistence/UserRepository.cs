using Microsoft.EntityFrameworkCore;
using Normetry.Application.Identity;

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

    public async Task<IReadOnlyList<string>> GetPermissionsAsync(
        string sub,
        CancellationToken cancellationToken
    )
    {
        var held = db
            .UserProfiles.Where(link =>
                db.Users.Any(user => user.Id == link.UserId && user.Sub == sub)
            )
            .Select(link => link.ProfileId);
        var profiles = db.Profiles.Where(profile => held.Contains(profile.Id));
        // Owner's grants_all covers every catalog row, present and future.
        return await db
            .Permissions.Where(permission =>
                profiles.Any(profile =>
                    profile.GrantsAll
                    || profile.GrantedPermissions.Any(granted =>
                        granted.Permission == permission.Name
                    )
                )
            )
            .Select(permission => permission.Name)
            .OrderBy(name => name)
            .ToListAsync(cancellationToken);
    }
}
