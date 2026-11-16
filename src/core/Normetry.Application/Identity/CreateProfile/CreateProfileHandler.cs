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
        // Two concurrent creates of one name both pass this check; the unique index refuses the
        // second at commit, which TransactionBehavior maps to the same refusal.
        if (await profiles.NameTakenAsync(profile.Name, cancellationToken))
        {
            return IdentityErrors.NameTaken;
        }
        profiles.Add(profile);
        return profile.Id;
    }
}
