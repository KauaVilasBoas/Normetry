using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Identity.GetProfile;

internal sealed class GetProfileHandler(IProfileRepository profiles)
    : IQueryHandler<GetProfileQuery, ProfileResponse>
{
    public async Task<Result<ProfileResponse>> HandleAsync(
        GetProfileQuery request,
        CancellationToken cancellationToken
    ) =>
        await profiles.GetAsync(request.Id, cancellationToken) is { } profile
            ? ProfileResponse.From(profile, await profiles.ListCatalogAsync(cancellationToken))
            : IdentityErrors.ProfileNotFound;
}
