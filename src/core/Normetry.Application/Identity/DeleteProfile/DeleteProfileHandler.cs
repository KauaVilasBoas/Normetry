using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Identity.DeleteProfile;

internal sealed class DeleteProfileHandler(IProfileRepository profiles)
    : ICommandHandler<DeleteProfileCommand>
{
    public async Task<Result> HandleAsync(
        DeleteProfileCommand request,
        CancellationToken cancellationToken
    )
    {
        if (await profiles.GetAsync(request.Id, cancellationToken) is not { } profile)
        {
            return IdentityErrors.ProfileNotFound;
        }
        // Owner is a system profile too, so the lockout guard holds here as well.
        if (profile.IsSystem)
        {
            return IdentityErrors.SystemProfile;
        }

        // The database removes its permissions and its user links (ON DELETE CASCADE).
        profiles.Remove(profile);
        return Result.Success();
    }
}
