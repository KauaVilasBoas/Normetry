using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Identity.RenameProfile;

internal sealed class RenameProfileHandler(IProfileRepository profiles)
    : ICommandHandler<RenameProfileCommand>
{
    public async Task<Result> HandleAsync(
        RenameProfileCommand request,
        CancellationToken cancellationToken
    )
    {
        if (await profiles.GetAsync(request.Id, cancellationToken) is not { } profile)
        {
            return IdentityErrors.ProfileNotFound;
        }
        if (profile.IsSystem)
        {
            return IdentityErrors.SystemProfile;
        }

        var previousName = profile.Name;
        profile.Rename(request.Name);
        // The check reads the saved rows, where this profile still has its previous name, so
        // keeping that name is no clash. A refusal rolls the rename back.
        if (
            profile.Name != previousName
            && await profiles.NameTakenAsync(profile.Name, cancellationToken)
        )
        {
            return IdentityErrors.NameTaken;
        }
        return Result.Success();
    }
}
