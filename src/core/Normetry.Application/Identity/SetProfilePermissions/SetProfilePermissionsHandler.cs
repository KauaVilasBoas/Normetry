using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Identity.SetProfilePermissions;

internal sealed class SetProfilePermissionsHandler(IProfileRepository profiles)
    : ICommandHandler<SetProfilePermissionsCommand, ProfileResponse>
{
    public async Task<Result<ProfileResponse>> HandleAsync(
        SetProfilePermissionsCommand request,
        CancellationToken cancellationToken
    )
    {
        // Locked before it is read, so two concurrent saves apply one after the other instead of
        // both adding the same rows.
        await profiles.LockAsync(request.Id, cancellationToken);
        if (await profiles.GetAsync(request.Id, cancellationToken) is not { } profile)
        {
            return IdentityErrors.ProfileNotFound;
        }
        if (profile.GrantsAll)
        {
            return IdentityErrors.OwnerLocked;
        }

        profile.SetPermissions(request.Permissions);
        return ProfileResponse.From(profile, []);
    }
}
