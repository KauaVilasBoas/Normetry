using Normetry.Application.Abstractions;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;

namespace Normetry.Application.Identity.AssignProfile;

internal sealed class AssignProfileHandler(
    IUserRepository users,
    IProfileRepository profiles,
    ITenantContext tenant
) : ICommandHandler<AssignProfileCommand>
{
    public async Task<Result> HandleAsync(
        AssignProfileCommand request,
        CancellationToken cancellationToken
    )
    {
        if (!await users.ExistsAsync(request.UserId, cancellationToken))
        {
            return IdentityErrors.UserNotFound;
        }
        if (await profiles.GetAsync(request.ProfileId, cancellationToken) is null)
        {
            return IdentityErrors.ProfileNotFound;
        }

        await users.AssignAsync(
            tenant.TenantId,
            request.UserId,
            request.ProfileId,
            cancellationToken
        );
        return Result.Success();
    }
}
