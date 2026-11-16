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

        var name = request.Name.Trim();
        if (name != profile.Name && await profiles.NameTakenAsync(name, cancellationToken))
        {
            return IdentityErrors.NameTaken;
        }
        profile.Rename(name);
        return Result.Success();
    }
}
