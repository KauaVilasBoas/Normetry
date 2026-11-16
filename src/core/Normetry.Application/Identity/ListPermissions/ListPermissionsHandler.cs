using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Identity.ListPermissions;

internal sealed class ListPermissionsHandler(IProfileRepository profiles)
    : IQueryHandler<ListPermissionsQuery, IReadOnlyList<PermissionResponse>>
{
    public async Task<Result<IReadOnlyList<PermissionResponse>>> HandleAsync(
        ListPermissionsQuery request,
        CancellationToken cancellationToken
    ) =>
        (await profiles.ListCatalogAsync(cancellationToken))
            .Select(name => new PermissionResponse(name))
            .ToArray();
}
