using Normetry.Application.Abstractions;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Normetry.Domain.Identity;

namespace Normetry.Application.Identity.CreateProfile;

internal sealed class CreateProfileHandler(IProfileRepository profiles, ITenantContext tenant)
    : ICommandHandler<CreateProfileCommand, Guid>
{
    public async Task<Result<Guid>> HandleAsync(
        CreateProfileCommand request,
        CancellationToken cancellationToken
    )
    {
        var profile = Profile.Create(tenant.TenantId, request.Name);
        // ponytail: check then insert; two concurrent creates of one name end in a 500 from the
        // unique index instead of a 409. Map the unique violation if that ever matters.
        if (await profiles.NameTakenAsync(profile.Name, cancellationToken))
        {
            return IdentityErrors.NameTaken;
        }
        profiles.Add(profile);
        return profile.Id;
    }
}
