using Normetry.Application.Abstractions;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Identity.GetCurrentUser;

internal sealed class GetCurrentUserHandler(ICurrentUser currentUser, ITenantContext tenant)
    : IQueryHandler<GetCurrentUserQuery, CurrentUserResponse>
{
    public Task<Result<CurrentUserResponse>> HandleAsync(
        GetCurrentUserQuery request,
        CancellationToken cancellationToken
    ) =>
        Task.FromResult<Result<CurrentUserResponse>>(
            new CurrentUserResponse(currentUser.UserId, tenant.TenantId, currentUser.Roles)
        );
}
