using Normetry.Application.Abstractions;
using Normetry.Application.Tenancy;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Identity.GetCurrentUser;

internal sealed class GetCurrentUserHandler(
    ICurrentUser currentUser,
    ITenantContext tenant,
    ITenantRepository tenants
) : IQueryHandler<GetCurrentUserQuery, CurrentUserResponse>
{
    public async Task<Result<CurrentUserResponse>> HandleAsync(
        GetCurrentUserQuery request,
        CancellationToken cancellationToken
    )
    {
        var row = await tenants.GetAsync(tenant.TenantId, cancellationToken);
        return new CurrentUserResponse(
            currentUser.UserId,
            tenant.TenantId,
            row?.Name,
            currentUser.Roles
        );
    }
}
