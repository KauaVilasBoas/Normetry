using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Identity.ListProfiles;

internal sealed class ListProfilesHandler(IProfileRepository profiles)
    : IQueryHandler<ListProfilesQuery, IReadOnlyList<ProfileListItem>>
{
    public async Task<Result<IReadOnlyList<ProfileListItem>>> HandleAsync(
        ListProfilesQuery request,
        CancellationToken cancellationToken
    ) =>
        Result<IReadOnlyList<ProfileListItem>>.Success(await profiles.ListAsync(cancellationToken));
}
