using Normetry.Application.Abstractions;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Identity.GetCurrentUserPermissions;

internal sealed class GetCurrentUserPermissionsHandler(
    IUserRepository users,
    ICurrentUser currentUser
) : IQueryHandler<GetCurrentUserPermissionsQuery, IReadOnlyList<string>>
{
    public async Task<Result<IReadOnlyList<string>>> HandleAsync(
        GetCurrentUserPermissionsQuery request,
        CancellationToken cancellationToken
    ) =>
        Result<IReadOnlyList<string>>.Success(
            await users.GetPermissionsAsync(currentUser.UserId, cancellationToken)
        );
}
