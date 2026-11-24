using Normetry.Application.Abstractions;
using Normetry.Application.Tenancy;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Identity.GetCurrentUser;

internal sealed class GetCurrentUserHandler(
    ICurrentUser currentUser,
    ITenantContext tenant,
    ITenantRepository tenants,
    IUserRepository users
) : IQueryHandler<GetCurrentUserQuery, CurrentUserResponse>
{
    public async Task<Result<CurrentUserResponse>> HandleAsync(
        GetCurrentUserQuery request,
        CancellationToken cancellationToken
    )
    {
        var row = await tenants.GetAsync(tenant.TenantId, cancellationToken);
        var profiles = await users.GetProfilesAsync(currentUser.Sub, cancellationToken);
        var permissions = await users.GetPermissionsAsync(currentUser.Sub, cancellationToken);
        return new CurrentUserResponse(
            currentUser.Sub,
            tenant.TenantId,
            row?.Name,
            permissions,
            profiles
        );
    }
}
